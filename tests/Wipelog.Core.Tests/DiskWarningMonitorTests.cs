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
using Wipelog.Core.Recording;
using Wipelog.Daemon.Notifications;
using Wipelog.Daemon.Storage;
using Xunit;

namespace Wipelog.Core.Tests;

public class DiskWarningMonitorTests
{
    private static readonly DateTimeOffset Base = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
    private const long Floor = 1000;

    private sealed class StubMonitor : IDiskMonitor
    {
        public long Free { get; set; } = 10_000;
        public long Total { get; set; } = 100_000;
        public DiskUsageReport GetCurrent() =>
            new(UsedBytes: Total - Free, TotalBytes: Total, FreeBytes: Free, MinFreeBytes: Floor,
                UsagePercent: Total > 0 ? (double)(Total - Free) / Total * 100 : 0);
        public Task<CleanupResult> CleanupIfNeededAsync(CancellationToken ct) => Task.FromResult(CleanupResult.Empty);
    }

    private sealed class CountingNotifications : INotificationService
    {
        public int Warnings;
        public Task NotifyClipSavedAsync(ClipFinalization clip, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyErrorAsync(string title, string message, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDiskWarningAsync(long freeGb, long minFreeGb, CancellationToken ct)
        {
            Warnings++;
            return Task.CompletedTask;
        }
        public Task NotifyAutoDeleteAsync(int deletedCount, long bytesFreed, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDiskFullFavoritesOnlyAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static (DiskWarningMonitor Monitor, StubMonitor Disk, CountingNotifications Notify) New(
        FakeClock clock, string statePath)
    {
        var disk = new StubMonitor();
        var notify = new CountingNotifications();
        var monitor = new DiskWarningMonitor(
            disk, notify, clock, NullLogger<DiskWarningMonitor>.Instance, statePath);
        return (monitor, disk, notify);
    }

    [Fact]
    public async Task Warns_once_then_suppresses_within_6h()
    {
        using var dir = new TempDirectory();
        var statePath = Path.Combine(dir.Path, "state.json");
        var clock = new FakeClock(Base);
        var (monitor, disk, notify) = New(clock, statePath);
        var ct = TestContext.Current.CancellationToken;
        disk.Free = 1100; // < floor*1.25 (1250) → low

        await monitor.CheckOnceAsync(ct);
        Assert.Equal(1, notify.Warnings);

        clock.Now = Base.AddHours(5); // still inside 6h
        await monitor.CheckOnceAsync(ct);
        Assert.Equal(1, notify.Warnings);

        clock.Now = Base.AddHours(7); // past the window
        await monitor.CheckOnceAsync(ct);
        Assert.Equal(2, notify.Warnings);
    }

    [Fact]
    public async Task No_warning_with_ample_free_space()
    {
        using var dir = new TempDirectory();
        var clock = new FakeClock(Base);
        var (monitor, disk, notify) = New(clock, Path.Combine(dir.Path, "state.json"));
        disk.Free = 50_000; // well above floor*1.25

        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, notify.Warnings);
    }

    [Fact]
    public async Task Warns_when_below_the_floor()
    {
        using var dir = new TempDirectory();
        var clock = new FakeClock(Base);
        var (monitor, disk, notify) = New(clock, Path.Combine(dir.Path, "state.json"));
        disk.Free = 500; // below the floor entirely

        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, notify.Warnings);
    }

    [Fact]
    public async Task Recovering_above_threshold_rearms_the_warning()
    {
        using var dir = new TempDirectory();
        var statePath = Path.Combine(dir.Path, "state.json");
        var clock = new FakeClock(Base);
        var (monitor, disk, notify) = New(clock, statePath);
        var ct = TestContext.Current.CancellationToken;

        disk.Free = 1100;
        await monitor.CheckOnceAsync(ct);
        Assert.Equal(1, notify.Warnings);

        // Cleanup freed space well above floor*1.25 → re-arm.
        clock.Now = Base.AddMinutes(1);
        disk.Free = 50_000;
        await monitor.CheckOnceAsync(ct);
        Assert.Equal(1, notify.Warnings);

        // Drops low again shortly after; re-armed, so it fires despite < 6h.
        clock.Now = Base.AddMinutes(2);
        disk.Free = 1100;
        await monitor.CheckOnceAsync(ct);
        Assert.Equal(2, notify.Warnings);
    }

    [Fact]
    public async Task No_warning_when_drive_unknown()
    {
        using var dir = new TempDirectory();
        var clock = new FakeClock(Base);
        var (monitor, disk, notify) = New(clock, Path.Combine(dir.Path, "state.json"));
        disk.Total = 0; // unknown drive
        disk.Free = 0;

        await monitor.CheckOnceAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, notify.Warnings);
    }
}
