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

using Wipelog.App.Services;
using Wipelog.Core.Status;
using Xunit;

namespace Wipelog.Core.Tests;

public class TrayIconHostTests
{
    [Fact]
    public void Offline_when_status_is_null()
    {
        var (icon, tooltip, menuState) = TrayIconHost.ComputeIconAndTooltip(
            status: null,
            now: new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(TrayIconHost.TrayIconKind.Offline, icon);
        Assert.Contains("offline", tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TrayIconHost.TrayMenuState.Offline, menuState);
    }

    [Fact]
    public void Idle_state_uses_idle_icon_and_clip_count_tooltip()
    {
        var status = new DaemonStatus("1.0.0", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DaemonState.Idle, CurrentRecording: null, ClipsRecordedThisSession: 3, LastError: null);

        var (icon, tooltip, menuState) = TrayIconHost.ComputeIconAndTooltip(status, DateTimeOffset.UtcNow);

        Assert.Equal(TrayIconHost.TrayIconKind.Idle, icon);
        Assert.Contains("Idle", tooltip);
        Assert.Contains("3 clips", tooltip);
        Assert.Equal(TrayIconHost.TrayMenuState.Idle, menuState);
    }

    [Fact]
    public void Idle_state_singular_clip_label()
    {
        var status = new DaemonStatus("1.0.0", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DaemonState.Idle, CurrentRecording: null, ClipsRecordedThisSession: 1, LastError: null);

        var (_, tooltip, _) = TrayIconHost.ComputeIconAndTooltip(status, DateTimeOffset.UtcNow);

        Assert.Contains("1 clip ", tooltip);
        Assert.DoesNotContain("1 clips", tooltip);
    }

    [Fact]
    public void Recording_state_uses_recording_icon_with_elapsed_in_tooltip()
    {
        var startedAt = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var now = startedAt.AddMinutes(1).AddSeconds(23);
        var status = new DaemonStatus("1.0.0", 1, startedAt, now,
            DaemonState.Recording,
            CurrentRecording: new RecordingStatus("MythicPlus", "Ara-Kara, City of Echoes", startedAt, 18),
            ClipsRecordedThisSession: 0,
            LastError: null);

        var (icon, tooltip, menuState) = TrayIconHost.ComputeIconAndTooltip(status, now);

        Assert.Equal(TrayIconHost.TrayIconKind.Recording, icon);
        Assert.Contains("Ara-Kara, City of Echoes", tooltip);
        Assert.Contains("1:23", tooltip);
        Assert.Equal(TrayIconHost.TrayMenuState.Recording, menuState);
    }
}
