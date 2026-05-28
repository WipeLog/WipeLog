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
using Wipelog.Core.Preferences;
using Xunit;

namespace Wipelog.Core.Tests;

public class SystemdUnitFileBuilderTests
{
    private static DaemonConfig Sample(string output = "/home/u/Videos/wow", string logDir = "/home/u/Logs") => new()
    {
        DaemonExecutablePath = "/home/u/bin/wipelog/Wipelog.Daemon",
        LogDirectory = logDir,
        LogPattern = "WoWCombatLog-*.txt",
        OutputDirectory = output,
        DatabasePath = "/home/u/.local/share/wipelog/clips.db",
        GpuScreenRecorderExecutable = "gpu-screen-recorder",
        CaptureTarget = "focused",
        Framerate = 60,
        Codec = "h264",
        Container = "mp4",
        AudioTracks = new[] { "default_output" },
        VideoQuality = "medium",
    };

    [Fact]
    public void Build_includes_unit_install_and_service_sections()
    {
        var content = SystemdUnitFileBuilder.Build(Sample());
        Assert.Contains("[Unit]", content);
        Assert.Contains("[Service]", content);
        Assert.Contains("[Install]", content);
        Assert.Contains("Type=simple", content);
        Assert.Contains("WantedBy=graphical-session.target", content);
        Assert.Contains("After=graphical-session.target", content);
        Assert.Contains("PartOf=graphical-session.target", content);
        Assert.Contains("Restart=on-failure", content);
    }

    [Fact]
    public void Build_quotes_path_with_spaces()
    {
        var withSpace = Sample(logDir: "/home/u/.wine/drive_c/Program Files (x86)/WoW/Logs");
        var content = SystemdUnitFileBuilder.Build(withSpace);
        Assert.Contains("--log-dir \"/home/u/.wine/drive_c/Program Files (x86)/WoW/Logs\"", content);
    }

    [Fact]
    public void Build_escapes_embedded_quotes()
    {
        var weird = Sample() with { OutputDirectory = "/home/u/Vid\"eos/wow" };
        var content = SystemdUnitFileBuilder.Build(weird);
        Assert.Contains("--output \"/home/u/Vid\\\"eos/wow\"", content);
    }

    [Fact]
    public void Build_includes_all_encoder_flags()
    {
        var c = Sample() with
        {
            Framerate = 144,
            Codec = "hevc",
            Container = "mkv",
            VideoQuality = "very_high",
            CaptureTarget = "DP-2",
            AudioTracks = new[] { "alsa_output.pci-0000_00_1f.3.analog-stereo" },
        };
        var content = SystemdUnitFileBuilder.Build(c);
        Assert.Contains("--framerate \"144\"", content);
        Assert.Contains("--codec \"hevc\"", content);
        Assert.Contains("--container \"mkv\"", content);
        Assert.Contains("--video-quality \"very_high\"", content);
        Assert.Contains("--window \"DP-2\"", content);
        Assert.Contains("--audio \"alsa_output.pci-0000_00_1f.3.analog-stereo\"", content);
    }

    [Fact]
    public void Build_emits_one_audio_flag_per_track()
    {
        var c = Sample() with
        {
            AudioTracks = new[]
            {
                "app:World of Warcraft",
                "app:spotify",
                "app:WEBRTC VoiceEngine",
                "app-inverse:World of Warcraft|spotify|WEBRTC VoiceEngine",
            },
        };
        var content = SystemdUnitFileBuilder.Build(c);
        Assert.Contains("--audio \"app:World of Warcraft\"", content);
        Assert.Contains("--audio \"app:spotify\"", content);
        Assert.Contains("--audio \"app:WEBRTC VoiceEngine\"", content);
        Assert.Contains("--audio \"app-inverse:World of Warcraft|spotify|WEBRTC VoiceEngine\"", content);
        // Four entries → four --audio occurrences (used to be one).
        Assert.Equal(4, CountOccurrences(content, "--audio "));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }

    [Fact]
    public void Build_omits_db_path_when_empty()
    {
        var c = Sample() with { DatabasePath = "" };
        var content = SystemdUnitFileBuilder.Build(c);
        Assert.DoesNotContain("--db-path", content);
    }

    [Fact]
    public void Build_emits_video_resolution_when_set()
    {
        var c = Sample() with { VideoResolution = "1080p" };
        var content = SystemdUnitFileBuilder.Build(c);
        Assert.Contains("--video-resolution \"1080p\"", content);
    }

    [Fact]
    public void Build_omits_video_resolution_when_source_or_null()
    {
        Assert.DoesNotContain("--video-resolution", SystemdUnitFileBuilder.Build(Sample()));
        Assert.DoesNotContain("--video-resolution",
            SystemdUnitFileBuilder.Build(Sample() with { VideoResolution = "source" }));
        Assert.DoesNotContain("--video-resolution",
            SystemdUnitFileBuilder.Build(Sample() with { VideoResolution = "" }));
    }

    [Fact]
    public void Build_emits_preroll_seconds_when_set()
    {
        var c = Sample() with { PreRollSeconds = 30 };
        var content = SystemdUnitFileBuilder.Build(c);
        Assert.Contains("--preroll-seconds \"30\"", content);
    }

    [Fact]
    public void Build_omits_preroll_seconds_when_null()
    {
        var c = Sample() with { PreRollSeconds = null };
        var content = SystemdUnitFileBuilder.Build(c);
        Assert.DoesNotContain("--preroll-seconds", content);
    }

    [Fact]
    public void IsValidForInstall_rejects_missing_required_paths()
    {
        Assert.False(new DaemonConfig().IsValidForInstall(out var err1));
        Assert.NotNull(err1);

        var partial = DaemonConfig.WithHomeDefaults() with { LogDirectory = "" };
        Assert.False(partial.IsValidForInstall(out var err2));
        Assert.Contains("log", err2!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsValidForInstall_passes_for_complete_config()
    {
        var c = Sample();
        Assert.True(c.IsValidForInstall(out var err));
        Assert.Null(err);
    }

    [Fact]
    public void IsValidForInstall_rejects_invalid_framerate_and_quality()
    {
        var fr = Sample() with { Framerate = 12 };
        Assert.False(fr.IsValidForInstall(out _));

        var q = Sample() with { VideoQuality = "lossless" };
        Assert.False(q.IsValidForInstall(out var err));
        Assert.Contains("medium", err!);
    }

    [Fact]
    public void IsValidForInstall_rejects_unknown_video_resolution()
    {
        var c = Sample() with { VideoResolution = "4k" };
        Assert.False(c.IsValidForInstall(out var err));
        Assert.Contains("resolution", err!, StringComparison.OrdinalIgnoreCase);

        Assert.True((Sample() with { VideoResolution = "1080p" }).IsValidForInstall(out _));
        Assert.True((Sample() with { VideoResolution = "source" }).IsValidForInstall(out _));
        Assert.True((Sample() with { VideoResolution = null }).IsValidForInstall(out _));
    }

    [Fact]
    public void IsValidForInstall_rejects_preroll_outside_gsr_range()
    {
        Assert.False((Sample() with { PreRollSeconds = 1 }).IsValidForInstall(out var low));
        Assert.Contains("Pre-roll", low!);

        Assert.False((Sample() with { PreRollSeconds = 86401 }).IsValidForInstall(out var high));
        Assert.Contains("Pre-roll", high!);

        Assert.True((Sample() with { PreRollSeconds = 30 }).IsValidForInstall(out _));
        Assert.True((Sample() with { PreRollSeconds = null }).IsValidForInstall(out _));
    }
}
