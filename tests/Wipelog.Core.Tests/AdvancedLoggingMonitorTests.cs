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
using Wipelog.Core.CombatLog;
using Wipelog.Core.Recording;
using Wipelog.Daemon.Logging;
using Wipelog.Daemon.Notifications;
using Wipelog.Daemon.Status;
using Xunit;

namespace Wipelog.Core.Tests;

public class AdvancedLoggingMonitorTests
{
    private sealed class CapturingNotifier : INotificationService
    {
        public List<(string Title, string Message)> Errors { get; } = new();
        public int ClipSavedCount { get; private set; }

        public Task NotifyClipSavedAsync(ClipFinalization clip, CancellationToken ct)
        {
            ClipSavedCount++;
            return Task.CompletedTask;
        }

        public Task NotifyErrorAsync(string title, string message, CancellationToken ct)
        {
            Errors.Add((title, message));
            return Task.CompletedTask;
        }

        public Task NotifyDiskWarningAsync(long freeGb, long minFreeGb, CancellationToken ct)
            => Task.CompletedTask;

        public Task NotifyAutoDeleteAsync(int deletedCount, long bytesFreed, CancellationToken ct)
            => Task.CompletedTask;

        public Task NotifyDiskFullFavoritesOnlyAsync(CancellationToken ct)
            => Task.CompletedTask;
    }

    private static DateTimeOffset Ts(int hour, int minute = 0) =>
        new(2026, 5, 19, hour, minute, 0, TimeSpan.Zero);

    private static HeartbeatState NewState() =>
        new(new FakeClock(Ts(12, 0)));

    [Fact]
    public void First_disabled_header_notifies_and_flips_state()
    {
        var state = NewState();
        var notifier = new CapturingNotifier();
        var monitor = new AdvancedLoggingMonitor(state, notifier,
            NullLogger<AdvancedLoggingMonitor>.Instance);

        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12), 21, AdvancedLogging: false));

        Assert.True(state.Snapshot().AdvancedLoggingDisabled);
        var err = Assert.Single(notifier.Errors);
        Assert.Contains("Advanced Combat Logging", err.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_disabled_headers_only_notify_once()
    {
        // WoW re-emits COMBAT_LOG_VERSION on every /reload. The monitor must
        // gate by state transition, not by event arrival, so a user with
        // ACL off doesn't get notified every few minutes during leveling.
        var state = NewState();
        var notifier = new CapturingNotifier();
        var monitor = new AdvancedLoggingMonitor(state, notifier,
            NullLogger<AdvancedLoggingMonitor>.Instance);

        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12, 0), 21, AdvancedLogging: false));
        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12, 5), 21, AdvancedLogging: false));
        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12, 10), 21, AdvancedLogging: false));

        Assert.Single(notifier.Errors);
        Assert.True(state.Snapshot().AdvancedLoggingDisabled);
    }

    [Fact]
    public void Enabling_after_disabled_clears_the_flag_and_does_not_notify()
    {
        var state = NewState();
        var notifier = new CapturingNotifier();
        var monitor = new AdvancedLoggingMonitor(state, notifier,
            NullLogger<AdvancedLoggingMonitor>.Instance);

        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12, 0), 21, AdvancedLogging: false));
        Assert.Single(notifier.Errors);
        Assert.True(state.Snapshot().AdvancedLoggingDisabled);

        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12, 5), 21, AdvancedLogging: true));

        Assert.False(state.Snapshot().AdvancedLoggingDisabled);
        Assert.Single(notifier.Errors);   // unchanged — enabling is silent
    }

    [Fact]
    public void Toggling_off_again_after_a_fix_re_arms_the_notifier()
    {
        // After the user fixes ACL and later disables it again (e.g. switching
        // characters, addon glitch) we should re-notify so the warning isn't
        // a one-shot for the entire daemon lifetime.
        var state = NewState();
        var notifier = new CapturingNotifier();
        var monitor = new AdvancedLoggingMonitor(state, notifier,
            NullLogger<AdvancedLoggingMonitor>.Instance);

        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12, 0), 21, AdvancedLogging: false));
        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12, 5), 21, AdvancedLogging: true));
        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(13, 0), 21, AdvancedLogging: false));

        Assert.Equal(2, notifier.Errors.Count);
        Assert.True(state.Snapshot().AdvancedLoggingDisabled);
    }

    [Fact]
    public void First_enabled_header_is_silent_and_state_stays_clean()
    {
        var state = NewState();
        var notifier = new CapturingNotifier();
        var monitor = new AdvancedLoggingMonitor(state, notifier,
            NullLogger<AdvancedLoggingMonitor>.Instance);

        monitor.OnCombatLogVersion(new CombatLogVersion(Ts(12), 21, AdvancedLogging: true));

        Assert.Empty(notifier.Errors);
        Assert.False(state.Snapshot().AdvancedLoggingDisabled);
    }
}
