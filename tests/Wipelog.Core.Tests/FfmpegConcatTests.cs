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

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class FfmpegConcatTests
{
    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return false;
            p.WaitForExit(3000);
            return p.HasExited && p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static async Task<bool> GenerateTinyMp4Async(string path, int seconds, string color, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // lavfi-generated coloured square, no audio, fixed gop so concat-copy
        // doesn't need re-encoding.
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add($"color=c={color}:s=160x120:r=10:d={seconds}");
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-g"); psi.ArgumentList.Add("10");
        psi.ArgumentList.Add(path);

        using var p = Process.Start(psi);
        if (p is null) return false;
        await p.WaitForExitAsync(ct);
        return p.ExitCode == 0 && File.Exists(path);
    }

    private static async Task<bool> GenerateMultiAudioMp4Async(
        string path, int seconds, string color, int audioTracks, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add($"color=c={color}:s=160x120:r=10:d={seconds}");
        for (int i = 0; i < audioTracks; i++)
        {
            // Distinct frequencies so the tracks aren't byte-identical, which
            // matches the real-world case of one source per app.
            var freq = 220 + (i * 110);
            psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i"); psi.ArgumentList.Add($"sine=frequency={freq}:duration={seconds}");
        }
        psi.ArgumentList.Add("-map"); psi.ArgumentList.Add("0:v");
        for (int i = 0; i < audioTracks; i++)
        {
            psi.ArgumentList.Add("-map"); psi.ArgumentList.Add($"{i + 1}:a");
        }
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-g"); psi.ArgumentList.Add("10");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add(path);

        using var p = Process.Start(psi);
        if (p is null) return false;
        await p.WaitForExitAsync(ct);
        return p.ExitCode == 0 && File.Exists(path);
    }

    private static async Task<int> CountAudioStreamsAsync(string path, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams"); psi.ArgumentList.Add("a");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("stream=index");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("csv=p=0");
        psi.ArgumentList.Add(path);

        using var p = Process.Start(psi);
        if (p is null) return -1;
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) return -1;
        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static async Task<IReadOnlyList<string>> GetAudioStreamTitlesAsync(string path, CancellationToken ct)
    {
        // mp4 muxes the per-stream title into the `udta.name` atom, so the
        // tag we want is `name`, not `title`. We probe both so this helper
        // also works on Matroska/WebM output if we ever switch container.
        var psi = new ProcessStartInfo
        {
            FileName = "ffprobe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-select_streams"); psi.ArgumentList.Add("a");
        psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("stream_tags=title,name");
        psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("default=nokey=0:noprint_wrappers=1");
        psi.ArgumentList.Add(path);

        using var p = Process.Start(psi);
        if (p is null) return Array.Empty<string>();
        var stdout = await p.StandardOutput.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0) return Array.Empty<string>();

        var titles = new List<string>();
        foreach (var raw in stdout.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("TAG:name=", StringComparison.OrdinalIgnoreCase))
            {
                titles.Add(line.Substring("TAG:name=".Length));
            }
            else if (line.StartsWith("TAG:title=", StringComparison.OrdinalIgnoreCase))
            {
                titles.Add(line.Substring("TAG:title=".Length));
            }
        }
        return titles;
    }

    [Fact]
    public async Task Returns_false_when_inputs_count_below_two()
    {
        using var dir = new TempDirectory();
        var output = Path.Combine(dir.Path, "out.mp4");

        var result = await FfmpegConcat.ConcatAsync(
            new[] { Path.Combine(dir.Path, "one.mp4") }, output,
            NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.False(result);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task Returns_false_when_input_missing()
    {
        using var dir = new TempDirectory();
        var output = Path.Combine(dir.Path, "out.mp4");

        var result = await FfmpegConcat.ConcatAsync(
            new[]
            {
                Path.Combine(dir.Path, "missing-1.mp4"),
                Path.Combine(dir.Path, "missing-2.mp4"),
            },
            output, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.False(result);
    }

    [Fact]
    public async Task Returns_false_when_ffmpeg_not_found()
    {
        using var dir = new TempDirectory();
        var a = Path.Combine(dir.Path, "a.mp4");
        var b = Path.Combine(dir.Path, "b.mp4");
        await File.WriteAllBytesAsync(a, new byte[] { 0 }, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(b, new byte[] { 0 }, TestContext.Current.CancellationToken);
        var output = Path.Combine(dir.Path, "out.mp4");

        var result = await FfmpegConcat.ConcatAsync(
            new[] { a, b }, output, NullLogger.Instance,
            TestContext.Current.CancellationToken,
            ffmpegPath: "/definitely/not/a/real/ffmpeg-binary");

        Assert.False(result);
    }

    [Fact]
    public async Task Concatenates_two_mp4_files_via_stream_copy()
    {
        if (!FfmpegAvailable())
        {
            // No system ffmpeg → skip without failing the suite.
            return;
        }
        using var dir = new TempDirectory();
        var preroll = Path.Combine(dir.Path, "preroll.mp4");
        var main = Path.Combine(dir.Path, "main.mp4");
        var output = Path.Combine(dir.Path, "out.mp4");

        Assert.True(await GenerateTinyMp4Async(preroll, seconds: 1, color: "red",
            TestContext.Current.CancellationToken));
        Assert.True(await GenerateTinyMp4Async(main, seconds: 2, color: "green",
            TestContext.Current.CancellationToken));

        var result = await FfmpegConcat.ConcatAsync(
            new[] { preroll, main }, output, NullLogger.Instance,
            TestContext.Current.CancellationToken);

        Assert.True(result);
        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);

        // The concat list file is a transient and must be cleaned up.
        Assert.False(File.Exists(output + ".concat.txt"));

        // Probe duration: combined should be roughly 3s (1 + 2).
        var duration = await FfmpegProbe.GetDurationSecondsAsync(output,
            NullLogger.Instance, TestContext.Current.CancellationToken);
        Assert.NotNull(duration);
        Assert.InRange(duration!.Value, 2.5, 3.5);
    }

    // Regression for the bug where the daemon-driven recordings ended up with
    // a single audio track even though gpu-screen-recorder was invoked with
    // four `-a` flags. Root cause: ffmpeg's `-f concat -c copy` uses default
    // stream selection (1 video + 1 audio) unless `-map 0` forces every
    // stream through. The test recording skips concat and saw all 4 tracks;
    // the live recording was concat'd with the pre-roll snippet and lost 3.
    [Fact]
    public async Task Preserves_all_audio_tracks_across_concat()
    {
        if (!FfmpegAvailable())
        {
            return;
        }
        using var dir = new TempDirectory();
        var preroll = Path.Combine(dir.Path, "preroll.mp4");
        var main = Path.Combine(dir.Path, "main.mp4");
        var output = Path.Combine(dir.Path, "out.mp4");

        const int trackCount = 4;
        Assert.True(await GenerateMultiAudioMp4Async(preroll, seconds: 1, color: "red",
            audioTracks: trackCount, TestContext.Current.CancellationToken));
        Assert.True(await GenerateMultiAudioMp4Async(main, seconds: 2, color: "green",
            audioTracks: trackCount, TestContext.Current.CancellationToken));

        var result = await FfmpegConcat.ConcatAsync(
            new[] { preroll, main }, output, NullLogger.Instance,
            TestContext.Current.CancellationToken);

        Assert.True(result);
        var audioStreams = await CountAudioStreamsAsync(output,
            TestContext.Current.CancellationToken);
        Assert.Equal(trackCount, audioStreams);
    }

    [Fact]
    public async Task Stamps_audio_track_titles_when_provided()
    {
        if (!FfmpegAvailable())
        {
            return;
        }
        using var dir = new TempDirectory();
        var preroll = Path.Combine(dir.Path, "preroll.mp4");
        var main = Path.Combine(dir.Path, "main.mp4");
        var output = Path.Combine(dir.Path, "out.mp4");

        var titles = new[] { "World of Warcraft", "spotify", "WEBRTC VoiceEngine", "System mix" };
        Assert.True(await GenerateMultiAudioMp4Async(preroll, seconds: 1, color: "red",
            audioTracks: titles.Length, TestContext.Current.CancellationToken));
        Assert.True(await GenerateMultiAudioMp4Async(main, seconds: 2, color: "green",
            audioTracks: titles.Length, TestContext.Current.CancellationToken));

        var result = await FfmpegConcat.ConcatAsync(
            new[] { preroll, main }, output, NullLogger.Instance,
            TestContext.Current.CancellationToken,
            audioTrackTitles: titles);

        Assert.True(result);
        var probedTitles = await GetAudioStreamTitlesAsync(output,
            TestContext.Current.CancellationToken);
        Assert.Equal(titles, probedTitles);
    }
}
