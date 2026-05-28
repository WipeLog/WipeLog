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

public class SystemdUnitFileParserTests
{
    private static DaemonConfig? Parse(string content) => SystemdUnitFileParser.Parse(content);

    private static DaemonConfig Sample() => new()
    {
        DaemonExecutablePath = "/home/u/bin/wipelog/Wipelog.Daemon",
        LogDirectory = "/home/u/Logs",
        LogPattern = "WoWCombatLog-*.txt",
        OutputDirectory = "/home/u/Videos/wow",
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
    public void Parse_returns_null_for_empty_input()
    {
        Assert.Null(Parse(""));
        Assert.Null(Parse("   "));
    }

    [Fact]
    public void Parse_returns_null_when_ExecStart_missing()
    {
        var noExec = "[Unit]\nDescription=Nope\n[Service]\nType=simple\n";
        Assert.Null(Parse(noExec));
    }

    [Fact]
    public void Roundtrip_preserves_all_fields_for_default_sample()
    {
        var original = Sample();
        var unit = SystemdUnitFileBuilder.Build(original);
        var parsed = Parse(unit);
        AssertEquivalent(original, parsed);
    }

    [Fact]
    public void Roundtrip_preserves_paths_with_spaces()
    {
        var original = Sample() with
        {
            LogDirectory = "/home/u/.wine/drive_c/Program Files (x86)/WoW/Logs",
            OutputDirectory = "/home/u/Videos/wow recordings",
        };
        var unit = SystemdUnitFileBuilder.Build(original);
        var parsed = Parse(unit);
        AssertEquivalent(original, parsed);
    }

    [Fact]
    public void Roundtrip_preserves_embedded_quotes_and_backslashes()
    {
        var original = Sample() with
        {
            OutputDirectory = "/home/u/Vid\"eos/wow",
            LogDirectory = "/home/u/Lo\\gs",
        };
        var unit = SystemdUnitFileBuilder.Build(original);
        var parsed = Parse(unit);
        AssertEquivalent(original, parsed);
    }

    [Fact]
    public void Roundtrip_preserves_all_encoder_flags()
    {
        var original = Sample() with
        {
            Framerate = 144,
            Codec = "hevc",
            Container = "mkv",
            VideoQuality = "very_high",
            CaptureTarget = "DP-2",
            AudioTracks = new[] { "alsa_output.pci-0000_00_1f.3.analog-stereo" },
        };
        var unit = SystemdUnitFileBuilder.Build(original);
        var parsed = Parse(unit);
        AssertEquivalent(original, parsed);
    }

    [Fact]
    public void Roundtrip_preserves_multiple_audio_tracks_in_order()
    {
        var original = Sample() with
        {
            AudioTracks = new[]
            {
                "app:World of Warcraft",
                "app:spotify",
                "app:WEBRTC VoiceEngine",
                "default_output",
            },
        };
        var unit = SystemdUnitFileBuilder.Build(original);
        var parsed = Parse(unit);
        Assert.NotNull(parsed);
        Assert.Equal(original.AudioTracks, parsed!.AudioTracks);
    }

    [Fact]
    public void Parse_handles_omitted_db_path()
    {
        var original = Sample() with { DatabasePath = "" };
        var unit = SystemdUnitFileBuilder.Build(original);
        Assert.DoesNotContain("--db-path", unit);
        var parsed = Parse(unit);
        Assert.NotNull(parsed);
        Assert.Equal("", parsed!.DatabasePath);
    }

    [Fact]
    public void Roundtrip_preserves_video_resolution()
    {
        var original = Sample() with { VideoResolution = "1080p" };
        var unit = SystemdUnitFileBuilder.Build(original);
        var parsed = Parse(unit);
        Assert.NotNull(parsed);
        Assert.Equal("1080p", parsed!.VideoResolution);
    }

    [Fact]
    public void Parse_returns_null_video_resolution_when_flag_absent()
    {
        var unit = SystemdUnitFileBuilder.Build(Sample());
        Assert.DoesNotContain("--video-resolution", unit);
        var parsed = Parse(unit);
        Assert.NotNull(parsed);
        Assert.Null(parsed!.VideoResolution);
    }

    [Fact]
    public void Roundtrip_preserves_preroll_seconds()
    {
        var original = Sample() with { PreRollSeconds = 45 };
        var unit = SystemdUnitFileBuilder.Build(original);
        var parsed = Parse(unit);
        Assert.NotNull(parsed);
        Assert.Equal(45, parsed!.PreRollSeconds);
    }

    [Fact]
    public void Parse_returns_null_preroll_when_flag_absent()
    {
        var unit = SystemdUnitFileBuilder.Build(Sample());
        Assert.DoesNotContain("--preroll-seconds", unit);
        var parsed = Parse(unit);
        Assert.NotNull(parsed);
        Assert.Null(parsed!.PreRollSeconds);
    }

    [Fact]
    public void Parse_reads_real_unit_file_with_legacy_naming()
    {
        // Captured from a real installed unit on disk (pre-rename paths). The
        // GUI should still recover these values rather than fall back to
        // defaults.
        var unit = """
            # Generated by Wipelog GUI. Edits made here will be
            # overwritten on the next install/reinstall.
            [Unit]
            Description=Wipelog daemon
            After=graphical-session.target
            PartOf=graphical-session.target

            [Service]
            Type=simple
            ExecStart="/home/u/bin/wowrecorder/WowRecorder.Daemon" \
                --log-dir "/home/u/wow-logs" \
                --log-pattern "WoWCombatLog-*.txt" \
                --output "/home/u/Videos/wow" \
                --db-path "/home/u/.local/share/wow-recorder/clips.db" \
                --executable "gpu-screen-recorder" \
                --window "DP-0" \
                --framerate "60" \
                --codec "h264" \
                --container "mp4" \
                --audio "default_output" \
                --video-quality "medium"
            Restart=on-failure
            """;
        var parsed = Parse(unit);
        Assert.NotNull(parsed);
        Assert.Equal("/home/u/bin/wowrecorder/WowRecorder.Daemon", parsed!.DaemonExecutablePath);
        Assert.Equal("/home/u/wow-logs", parsed.LogDirectory);
        Assert.Equal("/home/u/Videos/wow", parsed.OutputDirectory);
        Assert.Equal("/home/u/.local/share/wow-recorder/clips.db", parsed.DatabasePath);
        Assert.Equal("DP-0", parsed.CaptureTarget);
        Assert.Equal(60, parsed.Framerate);
        Assert.Equal("h264", parsed.Codec);
        Assert.Equal("mp4", parsed.Container);
        Assert.Equal("medium", parsed.VideoQuality);
        Assert.Equal(new[] { "default_output" }, parsed.AudioTracks);
    }

    private static void AssertEquivalent(DaemonConfig expected, DaemonConfig? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.DaemonExecutablePath, actual!.DaemonExecutablePath);
        Assert.Equal(expected.LogDirectory, actual.LogDirectory);
        Assert.Equal(expected.LogPattern, actual.LogPattern);
        Assert.Equal(expected.OutputDirectory, actual.OutputDirectory);
        Assert.Equal(expected.DatabasePath, actual.DatabasePath);
        Assert.Equal(expected.GpuScreenRecorderExecutable, actual.GpuScreenRecorderExecutable);
        Assert.Equal(expected.CaptureTarget, actual.CaptureTarget);
        Assert.Equal(expected.Framerate, actual.Framerate);
        Assert.Equal(expected.Codec, actual.Codec);
        Assert.Equal(expected.Container, actual.Container);
        Assert.Equal(expected.VideoQuality, actual.VideoQuality);
        Assert.Equal(expected.VideoResolution, actual.VideoResolution);
        Assert.Equal(expected.AudioTracks, actual.AudioTracks);
    }
}
