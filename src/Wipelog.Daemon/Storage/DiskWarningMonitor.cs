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

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Time;
using Wipelog.Daemon.Notifications;

namespace Wipelog.Daemon.Storage;

/// <summary>
/// Ticks every 60s and warns (once per 6h) when the drive's free space nears or
/// falls below the configured floor (free &lt; floor × 1.25). Climbing back above
/// that re-arms the warning. The eviction itself happens on the recording-start
/// path (<see cref="DiskMonitor.EnsureCapacityAsync"/>).
/// </summary>
internal sealed class DiskWarningMonitor : BackgroundService
{
    // Warn once free space is within 25% of the floor (or already below it).
    internal const double WarnAtFractionOfFloor = 1.25;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ReNotifyAfter = TimeSpan.FromHours(6);

    private readonly IDiskMonitor _monitor;
    private readonly INotificationService _notifications;
    private readonly IClock _clock;
    private readonly ILogger<DiskWarningMonitor> _logger;
    private readonly string _statePath;

    public DiskWarningMonitor(
        IDiskMonitor monitor,
        INotificationService notifications,
        IClock clock,
        ILogger<DiskWarningMonitor> logger,
        string? statePath = null)
    {
        _monitor = monitor;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
        _statePath = statePath ?? DefaultStatePath();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// One evaluation tick — internal so tests can drive it directly with a
    /// <see cref="FakeClock"/> instead of waiting on the timer.
    /// </summary>
    internal async Task CheckOnceAsync(CancellationToken ct)
    {
        DateTimeOffset? lastNotify;
        try
        {
            var report = _monitor.GetCurrent();
            lastNotify = LoadLastNotify();

            // Unknown drive (can't enforce) → nothing to warn about.
            if (report.TotalBytes == 0)
            {
                return;
            }

            var warnThreshold = (long)(report.MinFreeBytes * WarnAtFractionOfFloor);
            if (report.FreeBytes >= warnThreshold)
            {
                // Comfortably above the floor — re-arm so a future drop fires again.
                if (lastNotify is not null) SaveLastNotify(null);
                return;
            }

            if (lastNotify is { } at && _clock.Now - at < ReNotifyAfter)
            {
                return;
            }

            var freeGb = report.FreeBytes / (1024L * 1024 * 1024);
            var minFreeGb = report.MinFreeBytes / (1024L * 1024 * 1024);
            await _notifications
                .NotifyDiskWarningAsync(freeGb, minFreeGb, ct)
                .ConfigureAwait(false);
            SaveLastNotify(_clock.Now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Disk warning check failed.");
        }
    }

    private DateTimeOffset? LoadLastNotify()
    {
        try
        {
            if (!File.Exists(_statePath)) return null;
            using var fs = File.OpenRead(_statePath);
            return JsonSerializer.Deserialize<WarnState>(fs)?.LastNotifyAt;
        }
        catch
        {
            return null;
        }
    }

    private void SaveLastNotify(DateTimeOffset? at)
    {
        try
        {
            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _statePath + ".tmp";
            using (var fs = File.Create(tmp))
            {
                JsonSerializer.Serialize(fs, new WarnState(at));
            }
            File.Move(tmp, _statePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist disk-warning state.");
        }
    }

    private static string DefaultStatePath()
    {
        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cache))
        {
            cache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache");
        }
        return Path.Combine(cache, "wipelog", "disk-warning-state.json");
    }

    private sealed record WarnState(
        [property: JsonPropertyName("lastNotifyAt")] DateTimeOffset? LastNotifyAt);
}
