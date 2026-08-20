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

using Wipelog.App.ViewModels;
using Wipelog.Core.Status;
using Xunit;

namespace Wipelog.Core.Tests;

public class DaemonStatusViewModelTests
{
    [Fact]
    public void Project_offline_when_status_is_null()
    {
        var now = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var p = DaemonStatusViewModel.Project(null, now);

        Assert.Equal("Offline", p.Label);
        Assert.True(p.Offline);
        Assert.False(p.Recording);
        Assert.False(p.Idle);
        Assert.Contains("offline", p.Tooltip, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, p.ActivityLine);
        Assert.Equal(string.Empty, p.ElapsedLine);
    }

    [Fact]
    public void Project_idle_includes_clip_count()
    {
        var now = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var status = new DaemonStatus("1", 1, now.AddMinutes(-1), now,
            DaemonState.Idle, CurrentRecording: null, ClipsRecordedThisSession: 4, LastError: null);

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.Equal("Idle", p.Label);
        Assert.True(p.Idle);
        Assert.False(p.Offline);
        Assert.False(p.Recording);
        Assert.Contains("4 clips", p.Tooltip);
        Assert.Equal(string.Empty, p.ActivityLine);
        Assert.Equal(string.Empty, p.ElapsedLine);
    }

    [Fact]
    public void Project_recording_includes_elapsed()
    {
        var startedAt = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var now = startedAt.AddMinutes(2).AddSeconds(7);
        var status = new DaemonStatus("1", 1, startedAt.AddMinutes(-1), now,
            DaemonState.Recording,
            CurrentRecording: new RecordingStatus("RaidEncounter", "Ulgrax the Devourer", startedAt, null),
            ClipsRecordedThisSession: 0,
            LastError: null);

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.Equal("Recording", p.Label);
        Assert.True(p.Recording);
        Assert.False(p.Offline);
        Assert.False(p.Idle);
        Assert.Contains("Ulgrax the Devourer", p.Tooltip);
        Assert.Contains("2:07", p.Tooltip);
    }

    [Fact]
    public void Project_appends_last_error_when_set()
    {
        var now = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var status = new DaemonStatus("1", 1, now.AddMinutes(-1), now,
            DaemonState.Idle, CurrentRecording: null, ClipsRecordedThisSession: 0,
            LastError: "Recorder exited with code 1");

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.Contains("Last error: Recorder exited with code 1", p.Tooltip);
    }

    [Fact]
    public void Project_recording_surfaces_activity_and_elapsed_for_raid()
    {
        var startedAt = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var now = startedAt.AddMinutes(4).AddSeconds(32);
        var status = new DaemonStatus("1", 1, startedAt.AddMinutes(-1), now,
            DaemonState.Recording,
            CurrentRecording: new RecordingStatus("RaidEncounter", "Ulgrax the Devourer", startedAt, null),
            ClipsRecordedThisSession: 0,
            LastError: null);

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.Equal("Ulgrax the Devourer", p.ActivityLine);
        Assert.Equal("4:32", p.ElapsedLine);
    }

    [Fact]
    public void Project_recording_appends_keystone_level_for_mplus()
    {
        var startedAt = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var now = startedAt.AddMinutes(28).AddSeconds(5);
        var status = new DaemonStatus("1", 1, startedAt.AddMinutes(-1), now,
            DaemonState.Recording,
            CurrentRecording: new RecordingStatus("MythicPlus", "The Stonevault", startedAt, KeystoneLevel: 12),
            ClipsRecordedThisSession: 0,
            LastError: null);

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.Equal("The Stonevault +12", p.ActivityLine);
        Assert.Equal("28:05", p.ElapsedLine);
    }

    [Fact]
    public void Project_recording_uses_hms_format_past_one_hour()
    {
        var startedAt = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var now = startedAt.AddHours(1).AddMinutes(2).AddSeconds(15);
        var status = new DaemonStatus("1", 1, startedAt.AddMinutes(-1), now,
            DaemonState.Recording,
            CurrentRecording: new RecordingStatus("MythicPlus", "Mists of Tirna Scithe", startedAt, KeystoneLevel: 10),
            ClipsRecordedThisSession: 0,
            LastError: null);

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.Equal("1:02:15", p.ElapsedLine);
    }

    [Fact]
    public void Project_recording_clamps_negative_elapsed_to_zero()
    {
        var startedAt = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var now = startedAt.AddSeconds(-2); // clock skew, e.g., heartbeat before encounter start propagated
        var status = new DaemonStatus("1", 1, startedAt.AddMinutes(-1), now,
            DaemonState.Recording,
            CurrentRecording: new RecordingStatus("RaidEncounter", "Boss", startedAt, null),
            ClipsRecordedThisSession: 0,
            LastError: null);

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.Equal("0:00", p.ElapsedLine);
    }

    [Fact]
    public void Project_propagates_advanced_logging_disabled_flag()
    {
        var now = new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero);
        var status = new DaemonStatus("1", 1, now.AddMinutes(-1), now,
            DaemonState.Idle, CurrentRecording: null, ClipsRecordedThisSession: 0,
            LastError: null)
        {
            AdvancedLoggingDisabled = true,
        };

        var p = DaemonStatusViewModel.Project(status, now);

        Assert.True(p.AdvancedLoggingDisabled);
    }

    [Fact]
    public void Project_offline_has_advanced_logging_flag_false()
    {
        // We deliberately don't claim ACL is off when we can't talk to the
        // daemon — the warning should only appear once we have authoritative
        // log data.
        var p = DaemonStatusViewModel.Project(null,
            new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero));

        Assert.False(p.AdvancedLoggingDisabled);
    }

    [Fact]
    public void Project_propagates_recorder_error_in_idle_and_recording()
    {
        var now = new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero);
        var error = "Recorder process died mid-recording (exitCode=51)";

        var idle = new DaemonStatus("1", 1, now.AddMinutes(-1), now,
            DaemonState.Idle, CurrentRecording: null, ClipsRecordedThisSession: 0,
            LastError: null)
        { RecorderError = error };
        Assert.Equal(error, DaemonStatusViewModel.Project(idle, now).RecorderError);

        var recording = idle with
        {
            State = DaemonState.Recording,
            CurrentRecording = new RecordingStatus("RaidEncounter", "Boss", now.AddMinutes(-2), null),
        };
        Assert.Equal(error, DaemonStatusViewModel.Project(recording, now).RecorderError);
    }

    [Fact]
    public void Project_recorder_error_empty_when_absent_or_offline()
    {
        var now = new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero);
        var status = new DaemonStatus("1", 1, now.AddMinutes(-1), now,
            DaemonState.Idle, CurrentRecording: null, ClipsRecordedThisSession: 0,
            LastError: null);

        Assert.Equal(string.Empty, DaemonStatusViewModel.Project(status, now).RecorderError);
        Assert.Equal(string.Empty, DaemonStatusViewModel.Project(null, now).RecorderError);
    }
}
