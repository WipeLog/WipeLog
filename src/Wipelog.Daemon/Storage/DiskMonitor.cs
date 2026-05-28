// Wipelog — Linux-native World of Warcraft gameplay recorder.
// Copyright (C) 2026 Patrik - WipeLog-dev
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Microsoft.Extensions.Logging;
using Wipelog.Core.Recording;
using Wipelog.Core.Status;
using Wipelog.Core.Storage;
using Wipelog.Core.Time;
using Wipelog.Daemon.Notifications;
using Wipelog.Daemon.Status;
using Wipelog.Library;

namespace Wipelog.Daemon.Storage;

/// <summary>
/// Whole-drive usage snapshot. <see cref="MinFreeBytes"/> is the configured
/// free-space floor; <see cref="TotalBytes"/> == 0 means the drive couldn't be
/// probed (unknown).
/// </summary>
public sealed record DiskUsageReport(
    long UsedBytes, long TotalBytes, long FreeBytes, long MinFreeBytes, double UsagePercent)
{
    public static readonly DiskUsageReport Unknown = new(0, 0, 0, 0, 0);
}

/// <summary>Outcome of an auto-delete sweep.</summary>
public sealed record CleanupResult(int DeletedCount, long BytesFreed, IReadOnlyList<string> DeletedPaths)
{
    public static readonly CleanupResult Empty = new(0, 0, Array.Empty<string>());
}

public interface IDiskMonitor
{
    /// <summary>Current whole-drive usage, cached for 60s between probes.</summary>
    DiskUsageReport GetCurrent();

    /// <summary>
    /// If the drive has less than the configured free-space floor available,
    /// delete the oldest non-favorited clips until the floor is restored. No-op
    /// when free space is fine or the drive is unknown. Favorites are never deleted.
    /// </summary>
    Task<CleanupResult> CleanupIfNeededAsync(CancellationToken ct);
}

/// <summary>
/// Keeps a configured amount of free space on the drive holding the clips by
/// evicting the oldest non-favorited clips on the next recording attempt (via
/// <see cref="IRecordingStartGate"/>) whenever free space has dropped below the
/// floor — no matter what consumed it. A plain singleton; the periodic low-space
/// warning lives in <see cref="DiskWarningMonitor"/>.
/// </summary>
internal sealed class DiskMonitor : IDiskMonitor, IRecordingStartGate
{
    internal const long BytesPerGigabyte = 1024L * 1024 * 1024;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const int DeleteBatch = 50;

    private readonly string _outputDirectory;
    private readonly long _minFreeBytes;
    private readonly IClipRepository _repo;
    private readonly INotificationService _notifications;
    private readonly HeartbeatState _heartbeat;
    private readonly IDiskSpaceProbe _diskSpace;
    private readonly IClock _clock;
    private readonly ILogger<DiskMonitor> _logger;

    private readonly object _gate = new();
    private DiskUsageReport? _cached;
    private DateTimeOffset _cachedAt;

    /// <summary>Convert a GB figure from preferences into bytes.</summary>
    public static long GigabytesToBytes(long gigabytes) => Math.Max(0, gigabytes) * BytesPerGigabyte;

    /// <summary>
    /// How many bytes must be freed to bring free space back up to the floor
    /// (0 when already at/above it). Pure helper for the cleanup loop.
    /// </summary>
    internal static long DeficitBytes(long freeBytes, long minFreeBytes) => Math.Max(0, minFreeBytes - freeBytes);

    public DiskMonitor(
        string outputDirectory,
        long minFreeBytes,
        IClipRepository repo,
        INotificationService notifications,
        HeartbeatState heartbeat,
        IDiskSpaceProbe diskSpace,
        IClock clock,
        ILogger<DiskMonitor> logger)
    {
        _outputDirectory = outputDirectory;
        _minFreeBytes = Math.Max(0, minFreeBytes);
        _repo = repo;
        _notifications = notifications;
        _heartbeat = heartbeat;
        _diskSpace = diskSpace;
        _clock = clock;
        _logger = logger;
    }

    public DiskUsageReport GetCurrent()
    {
        lock (_gate)
        {
            if (_cached is { } c && _clock.Now - _cachedAt < CacheTtl)
            {
                return c;
            }
        }
        return Measure();
    }

    public Task EnsureCapacityAsync(CancellationToken ct) => CleanupIfNeededAsync(ct);

    public async Task<CleanupResult> CleanupIfNeededAsync(CancellationToken ct)
    {
        var report = Measure();
        // Can't enforce a free-space floor without knowing the drive size, and
        // nothing to do when there's already enough free space.
        if (report.TotalBytes == 0 || report.FreeBytes >= _minFreeBytes)
        {
            return CleanupResult.Empty;
        }

        _logger.LogInformation(
            "Free space {Free} bytes is below the floor {Floor} bytes — evicting old clips.",
            report.FreeBytes, _minFreeBytes);

        // Project free space upward as we delete (each clip frees its bytes)
        // rather than re-probing every iteration, so the loop terminates
        // deterministically. A single final Measure() reconciles afterwards.
        long projectedFree = report.FreeBytes;
        long bytesFreed = 0;
        var deletedPaths = new List<string>();
        bool favoritesOnly = false;

        while (projectedFree < _minFreeBytes)
        {
            var candidates = await _repo.GetOldestNonFavoritedClipsAsync(DeleteBatch, ct).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                favoritesOnly = true;
                break;
            }

            var idsThisRound = new List<long>();
            foreach (var clip in candidates)
            {
                idsThisRound.Add(clip.Id);
                deletedPaths.Add(clip.VideoPath);
                bytesFreed += clip.FileSizeBytes;
                projectedFree += clip.FileSizeBytes;
                if (projectedFree >= _minFreeBytes) break;
            }

            await _repo.DeleteClipsAsync(idsThisRound, ct).ConfigureAwait(false);

            // Exhausted every non-favorited clip and still short on space: the
            // rest are favorites (or non-clip data is occupying the drive).
            if (candidates.Count < DeleteBatch && projectedFree < _minFreeBytes)
            {
                favoritesOnly = true;
                break;
            }
        }

        // Refresh cache + heartbeat against actual on-disk state.
        Measure();

        if (favoritesOnly)
        {
            _logger.LogWarning("Free space still below the floor but all remaining clips are favorited.");
            await _notifications.NotifyDiskFullFavoritesOnlyAsync(ct).ConfigureAwait(false);
        }

        if (deletedPaths.Count > 0)
        {
            _logger.LogInformation(
                "Auto-deleted {Count} clips ({Bytes} bytes) to keep the drive's free-space floor.",
                deletedPaths.Count, bytesFreed);
            await _notifications.NotifyAutoDeleteAsync(deletedPaths.Count, bytesFreed, ct).ConfigureAwait(false);
        }

        return new CleanupResult(deletedPaths.Count, bytesFreed, deletedPaths);
    }

    private DiskUsageReport Measure()
    {
        // The free-space floor is about the whole drive, so we read the drive's
        // real free/total — not the clip directory's size. (Per-clip sizes for
        // eviction come from the DB.)
        var disk = _diskSpace.Probe(_outputDirectory);
        DiskUsageReport report;
        if (disk is null || disk.TotalBytes <= 0)
        {
            report = DiskUsageReport.Unknown with { MinFreeBytes = _minFreeBytes };
        }
        else
        {
            long total = disk.TotalBytes;
            long free = Math.Clamp(disk.FreeBytes, 0, total);
            long used = total - free;
            double percent = (double)used / total * 100.0;
            report = new DiskUsageReport(used, total, free, _minFreeBytes, percent);
        }

        lock (_gate)
        {
            _cached = report;
            _cachedAt = _clock.Now;
        }
        _heartbeat.SetDiskUsage(new DiskUsageInfo(
            report.UsedBytes, report.TotalBytes, report.FreeBytes, report.MinFreeBytes, report.UsagePercent));
        return report;
    }
}
