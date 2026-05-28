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

using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Wipelog.Core.Storage;
using Wipelog.Daemon.Notifications;
using Wipelog.Daemon.Status;
using Wipelog.Daemon.Storage;
using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class DiskMonitorTests
{
    private static readonly DateTimeOffset Base = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeDiskSpaceProbe : IDiskSpaceProbe
    {
        private readonly DiskSpaceInfo? _info;
        public FakeDiskSpaceProbe(DiskSpaceInfo? info) => _info = info;
        public DiskSpaceInfo? Probe(string path) => _info;
    }

    private sealed class CapturingNotifications : INotificationService
    {
        public int Warnings;
        public int AutoDeletes;
        public int FavoritesFull;
        public int LastDeletedCount;
        public long LastBytesFreed;

        public Task NotifyClipSavedAsync(ClipFinalization clip, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyErrorAsync(string title, string message, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDiskWarningAsync(long freeGb, long minFreeGb, CancellationToken ct)
        {
            Warnings++;
            return Task.CompletedTask;
        }
        public Task NotifyAutoDeleteAsync(int deletedCount, long bytesFreed, CancellationToken ct)
        {
            AutoDeletes++;
            LastDeletedCount = deletedCount;
            LastBytesFreed = bytesFreed;
            return Task.CompletedTask;
        }
        public Task NotifyDiskFullFavoritesOnlyAsync(CancellationToken ct)
        {
            FavoritesFull++;
            return Task.CompletedTask;
        }
    }

    // Output dir and the SQLite DB live in separate places, matching production.
    private static string NewOutputDir(TempDirectory dir)
    {
        var output = Path.Combine(dir.Path, "out");
        Directory.CreateDirectory(output);
        return output;
    }

    private static ClipRepository NewRepo(TempDirectory dir) =>
        new(new ClipDatabase(Path.Combine(dir.Path, "clips.db"), NullLogger<ClipDatabase>.Instance));

    private static async Task<long> CreateClipAsync(
        ClipRepository repo, string dir, string name, int size, DateTimeOffset startedAt, bool favorite)
    {
        var videoPath = Path.Combine(dir, name);
        await File.WriteAllBytesAsync(videoPath, new byte[size], TestContext.Current.CancellationToken);
        var clip = new ClipRecord(
            Id: 0,
            VideoPath: videoPath,
            SidecarPath: videoPath + ".json",
            ContentType: ContentType.RaidEncounter,
            DisplayName: name,
            Difficulty: "Mythic",
            EncounterId: 2902,
            KeystoneLevel: null,
            Success: true,
            StartedAt: startedAt,
            EndedAt: startedAt.AddMinutes(5),
            DurationSeconds: 300,
            FileSizeBytes: size,
            Favorite: false,
            IndexedAt: startedAt);
        var id = await repo.InsertOrUpdateClipAsync(clip, TestContext.Current.CancellationToken);
        if (favorite) await repo.SetFavoriteAsync(id, true, TestContext.Current.CancellationToken);
        return id;
    }

    private static DiskMonitor NewMonitor(
        string dir, ClipRepository repo, long minFreeBytes, INotificationService notifications,
        FakeClock clock, DiskSpaceInfo? disk)
        => new(dir, minFreeBytes, repo, notifications, new HeartbeatState(clock),
            new FakeDiskSpaceProbe(disk), clock, NullLogger<DiskMonitor>.Instance);

    [Theory]
    [InlineData(700, 1000, 300)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1500, 1000, 0)]
    [InlineData(0, 1000, 1000)]
    public void DeficitBytes_is_shortfall_to_floor(long free, long floor, long expected)
        => Assert.Equal(expected, DiskMonitor.DeficitBytes(free, floor));

    [Fact]
    public async Task CleanupIfNeeded_noop_when_free_space_is_fine()
    {
        using var dir = new TempDirectory();
        var output = NewOutputDir(dir);
        var repo = NewRepo(dir);
        var ct = TestContext.Current.CancellationToken;
        await CreateClipAsync(repo, output, "a.mp4", 100, Base, favorite: false);

        var notifications = new CapturingNotifications();
        // free 5000 >= floor 1000 → nothing to do.
        var monitor = NewMonitor(output, repo, minFreeBytes: 1000, notifications, new FakeClock(Base),
            new DiskSpaceInfo(FreeBytes: 5000, TotalBytes: 10000));

        var result = await monitor.CleanupIfNeededAsync(ct);

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, notifications.AutoDeletes);
        Assert.True(File.Exists(Path.Combine(output, "a.mp4")));
    }

    [Fact]
    public async Task CleanupIfNeeded_evicts_oldest_until_floor_restored()
    {
        using var dir = new TempDirectory();
        var output = NewOutputDir(dir);
        var repo = NewRepo(dir);
        var ct = TestContext.Current.CancellationToken;
        // Free is 700, floor 1000 → must free 300. Each clip frees 100 → delete 3
        // oldest (700→800→900→1000).
        var oldest = await CreateClipAsync(repo, output, "1.mp4", 100, Base, favorite: false);
        var second = await CreateClipAsync(repo, output, "2.mp4", 100, Base.AddMinutes(1), favorite: false);
        var third = await CreateClipAsync(repo, output, "3.mp4", 100, Base.AddMinutes(2), favorite: false);
        await CreateClipAsync(repo, output, "4.mp4", 100, Base.AddMinutes(3), favorite: false);
        await CreateClipAsync(repo, output, "5.mp4", 100, Base.AddMinutes(4), favorite: false);

        var notifications = new CapturingNotifications();
        var monitor = NewMonitor(output, repo, minFreeBytes: 1000, notifications, new FakeClock(Base),
            new DiskSpaceInfo(FreeBytes: 700, TotalBytes: 100000));

        var result = await monitor.CleanupIfNeededAsync(ct);

        Assert.Equal(3, result.DeletedCount);
        Assert.Equal(1, notifications.AutoDeletes);
        Assert.Equal(0, notifications.FavoritesFull);
        Assert.False(File.Exists(Path.Combine(output, "1.mp4")));
        Assert.False(File.Exists(Path.Combine(output, "3.mp4")));
        Assert.True(File.Exists(Path.Combine(output, "4.mp4")));
        Assert.True(File.Exists(Path.Combine(output, "5.mp4")));
        Assert.Null(await repo.GetClipAsync(oldest, ct));
        Assert.Null(await repo.GetClipAsync(second, ct));
        Assert.Null(await repo.GetClipAsync(third, ct));
    }

    [Fact]
    public async Task CleanupIfNeeded_fires_even_when_clips_are_not_what_filled_the_disk()
    {
        using var dir = new TempDirectory();
        var output = NewOutputDir(dir);
        var repo = NewRepo(dir);
        var ct = TestContext.Current.CancellationToken;
        // Tiny clip library, but the drive is low on free space (other data).
        // Floor 1000, free 950 → free one clip (100) to clear the floor.
        await CreateClipAsync(repo, output, "1.mp4", 100, Base, favorite: false);
        await CreateClipAsync(repo, output, "2.mp4", 100, Base.AddMinutes(1), favorite: false);

        var notifications = new CapturingNotifications();
        var monitor = NewMonitor(output, repo, minFreeBytes: 1000, notifications, new FakeClock(Base),
            new DiskSpaceInfo(FreeBytes: 950, TotalBytes: 500000));

        var result = await monitor.CleanupIfNeededAsync(ct);

        Assert.Equal(1, result.DeletedCount);
        Assert.False(File.Exists(Path.Combine(output, "1.mp4"))); // oldest
        Assert.True(File.Exists(Path.Combine(output, "2.mp4")));
    }

    [Fact]
    public async Task CleanupIfNeeded_never_deletes_favorites()
    {
        using var dir = new TempDirectory();
        var output = NewOutputDir(dir);
        var repo = NewRepo(dir);
        var ct = TestContext.Current.CancellationToken;
        await CreateClipAsync(repo, output, "1.mp4", 100, Base, favorite: true);
        await CreateClipAsync(repo, output, "2.mp4", 100, Base.AddMinutes(1), favorite: true);

        var notifications = new CapturingNotifications();
        var monitor = NewMonitor(output, repo, minFreeBytes: 1000, notifications, new FakeClock(Base),
            new DiskSpaceInfo(FreeBytes: 100, TotalBytes: 100000));

        var result = await monitor.CleanupIfNeededAsync(ct);

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(0, notifications.AutoDeletes);
        Assert.Equal(1, notifications.FavoritesFull);
        Assert.True(File.Exists(Path.Combine(output, "1.mp4")));
        Assert.True(File.Exists(Path.Combine(output, "2.mp4")));
    }

    [Fact]
    public async Task CleanupIfNeeded_noop_when_drive_unknown()
    {
        using var dir = new TempDirectory();
        var output = NewOutputDir(dir);
        var repo = NewRepo(dir);
        var ct = TestContext.Current.CancellationToken;
        await CreateClipAsync(repo, output, "a.mp4", 100, Base, favorite: false);

        // Probe returns null → can't enforce a floor → no-op.
        var monitor = NewMonitor(output, repo, minFreeBytes: 1000, new CapturingNotifications(),
            new FakeClock(Base), disk: null);

        var result = await monitor.CleanupIfNeededAsync(ct);

        Assert.Equal(0, result.DeletedCount);
        Assert.True(File.Exists(Path.Combine(output, "a.mp4")));
    }

    [Fact]
    public void GetCurrent_reports_whole_disk_usage()
    {
        using var dir = new TempDirectory();
        var output = NewOutputDir(dir);
        var monitor = NewMonitor(output, NewRepo(dir), minFreeBytes: 1000, new CapturingNotifications(),
            new FakeClock(Base), new DiskSpaceInfo(FreeBytes: 4750, TotalBytes: 10000));

        var report = monitor.GetCurrent();

        Assert.Equal(5250, report.UsedBytes); // total - free
        Assert.Equal(10000, report.TotalBytes);
        Assert.Equal(4750, report.FreeBytes);
        Assert.Equal(1000, report.MinFreeBytes);
        Assert.Equal(52.5, report.UsagePercent);
    }

    [Fact]
    public void GetCurrent_reports_unknown_when_probe_fails()
    {
        using var dir = new TempDirectory();
        var monitor = NewMonitor(NewOutputDir(dir), NewRepo(dir), minFreeBytes: 1000,
            new CapturingNotifications(), new FakeClock(Base), disk: null);

        var report = monitor.GetCurrent();

        Assert.Equal(0, report.TotalBytes);
        Assert.Equal(1000, report.MinFreeBytes);
    }

    [Fact]
    public void GetCurrent_caches_for_60_seconds()
    {
        using var dir = new TempDirectory();
        var output = NewOutputDir(dir);
        var clock = new FakeClock(Base);
        // A mutable fake would be overkill; reuse the cache semantics: first call
        // probes, subsequent within TTL return the same snapshot.
        var monitor = NewMonitor(output, NewRepo(dir), 1000, new CapturingNotifications(), clock,
            new DiskSpaceInfo(FreeBytes: 4000, TotalBytes: 10000));

        var first = monitor.GetCurrent();
        clock.Now = Base.AddSeconds(59);
        Assert.Same(first, monitor.GetCurrent());
        clock.Now = Base.AddSeconds(61);
        // Past TTL it re-measures (new instance, same values from the static fake).
        var third = monitor.GetCurrent();
        Assert.Equal(first.FreeBytes, third.FreeBytes);
    }

    [Fact]
    public void SystemDiskSpaceProbe_reports_real_drive_for_existing_path()
    {
        using var dir = new TempDirectory();
        var info = SystemDiskSpaceProbe.Instance.Probe(dir.Path);

        Assert.NotNull(info);
        Assert.True(info!.TotalBytes > 0);
        Assert.True(info.FreeBytes >= 0);
        Assert.True(info.FreeBytes <= info.TotalBytes);
    }

    [Fact]
    public void SystemDiskSpaceProbe_resolves_through_a_not_yet_created_path()
    {
        using var dir = new TempDirectory();
        var nested = Path.Combine(dir.Path, "does", "not", "exist", "yet");
        var info = SystemDiskSpaceProbe.Instance.Probe(nested);

        Assert.NotNull(info);
        Assert.True(info!.TotalBytes > 0);
    }
}
