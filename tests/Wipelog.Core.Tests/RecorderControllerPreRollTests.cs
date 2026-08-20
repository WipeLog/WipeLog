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
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecorderControllerPreRollTests
{
    private static DateTimeOffset T(int hour, int minute = 0, int second = 0) =>
        new(2026, 5, 9, hour, minute, second, TimeSpan.Zero);

    /// <summary>
    /// Minimal stub: SaveAsync drops a tiny payload file in a directory and
    /// hands its path back to the controller. StartAsync / StopAsync are
    /// counters. ExitedUnexpectedly is wired but unused.
    /// </summary>
    private sealed class StubPreRollRecorder : IPreRollRecorder
    {
        public RecorderState State { get; set; } = RecorderState.Idle;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int SaveCount { get; private set; }
        public string OutputDirectory { get; }
        public string PayloadText { get; init; } = "fake-preroll";
        public Func<Task<string?>>? OnSave { get; init; }

        public StubPreRollRecorder(string outputDirectory)
        {
            OutputDirectory = outputDirectory;
            Directory.CreateDirectory(outputDirectory);
        }

        public event EventHandler<RecorderExitedUnexpectedlyEventArgs>? ExitedUnexpectedly;

        public Task StartAsync(CancellationToken ct)
        {
            StartCount++;
            State = RecorderState.Recording;
            return Task.CompletedTask;
        }

        public async Task<string?> SaveAsync(CancellationToken ct)
        {
            SaveCount++;
            if (OnSave is not null)
            {
                return await OnSave().ConfigureAwait(false);
            }
            var path = Path.Combine(OutputDirectory, $"replay-{SaveCount:000}.mp4");
            await File.WriteAllTextAsync(path, PayloadText, ct).ConfigureAwait(false);
            return path;
        }

        public Task StopAsync(CancellationToken ct)
        {
            StopCount++;
            State = RecorderState.Idle;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal void Raise(RecorderExitedUnexpectedlyEventArgs args) =>
            ExitedUnexpectedly?.Invoke(this, args);
    }

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

    private static async Task<bool> GenerateTinyMp4Async(
        string path, int seconds, string color, CancellationToken ct)
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
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-g"); psi.ArgumentList.Add("10");
        psi.ArgumentList.Add(path);
        using var p = Process.Start(psi);
        if (p is null) return false;
        await p.WaitForExitAsync(ct);
        return p.ExitCode == 0 && File.Exists(path);
    }

    [Fact]
    public async Task Pre_roll_lifecycle_is_started_and_stopped_around_run()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var preRoll = new StubPreRollRecorder(preRollDir);
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(), dir.Path,
            NullLogger<RecorderController>.Instance,
            preRoll: preRoll);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        Assert.Equal(1, preRoll.StartCount);
        Assert.Equal(1, preRoll.StopCount);
        Assert.Equal(0, preRoll.SaveCount);  // no encounters fired
    }

    [Fact]
    public async Task Save_is_called_once_per_encounter_start()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        // Hand back null so the controller's concat path doesn't try to run
        // ffmpeg in this lifecycle-only assertion.
        var preRoll = new StubPreRollRecorder(preRollDir)
        {
            OnSave = () => Task.FromResult<string?>(null),
        };
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(), dir.Path,
            NullLogger<RecorderController>.Instance,
            preRoll: preRoll);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        Assert.Equal(1, preRoll.SaveCount);
    }

    [Fact]
    public async Task Sidecar_records_pre_roll_seconds_when_concat_succeeds()
    {
        if (!FfmpegAvailable())
        {
            return; // skip when ffmpeg isn't on PATH
        }

        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var preRollFile = Path.Combine(preRollDir, "replay-001.mp4");
        Directory.CreateDirectory(preRollDir);
        // 1s red square as the pre-roll.
        Assert.True(await GenerateTinyMp4Async(preRollFile, seconds: 1, color: "red",
            TestContext.Current.CancellationToken));

        var preRoll = new StubPreRollRecorder(preRollDir)
        {
            OnSave = () => Task.FromResult<string?>(preRollFile),
        };

        // Use a custom recorder that writes a 2s green square to outputPath
        // on StartAsync so the controller has a real mp4 to concat with.
        var stubRecorder = new StubRecorder
        {
            OnStart = async path =>
            {
                Assert.True(await GenerateTinyMp4Async(path, seconds: 2, color: "green",
                    TestContext.Current.CancellationToken));
            },
        };

        var controller = new RecorderController(
            new EncounterDetector(), stubRecorder, new DefaultClipNamingStrategy(), dir.Path,
            NullLogger<RecorderController>.Instance,
            preRoll: preRoll);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        // Pre-roll source should be gone — TryDeletePreRoll runs after concat.
        Assert.False(File.Exists(preRollFile),
            "Pre-roll source file should be cleaned up after concat.");

        var files = Directory.GetFiles(dir.Path).OrderBy(f => f).ToList();
        var json = files.Single(f => f.EndsWith(".json", StringComparison.Ordinal)
                                      && !f.EndsWith(".events.json", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(File.ReadAllText(json));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("preRollSeconds", out var preRollProp),
            "Sidecar should record preRollSeconds when concat succeeded.");
        Assert.InRange(preRollProp.GetDouble(), 0.5, 1.5);

        // The events sidecar must mirror the offset so the UI can shift event
        // X positions / seek targets into the video file's timebase.
        var eventsJson = files.Single(f => f.EndsWith(".events.json", StringComparison.Ordinal));
        using var eventsDoc = JsonDocument.Parse(File.ReadAllText(eventsJson));
        Assert.True(eventsDoc.RootElement.TryGetProperty("preRollSeconds", out var eventsPreRoll),
            "Events sidecar should record preRollSeconds when concat succeeded.");
        Assert.InRange(eventsPreRoll.GetDouble(), 0.5, 1.5);

        // The concat temp file must not be left behind.
        Assert.DoesNotContain(files, f => f.EndsWith(".concat.mp4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sidecar_omits_pre_roll_when_save_returns_null()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var preRoll = new StubPreRollRecorder(preRollDir)
        {
            OnSave = () => Task.FromResult<string?>(null),
        };
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(), dir.Path,
            NullLogger<RecorderController>.Instance,
            preRoll: preRoll);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var files = Directory.GetFiles(dir.Path);
        var json = files.Single(f => f.EndsWith(".json", StringComparison.Ordinal)
                                      && !f.EndsWith(".events.json", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(File.ReadAllText(json));
        Assert.False(doc.RootElement.TryGetProperty("preRollSeconds", out _),
            "preRollSeconds should be absent when no pre-roll was attached.");

        var eventsJson = files.SingleOrDefault(f => f.EndsWith(".events.json", StringComparison.Ordinal));
        if (eventsJson is not null)
        {
            using var eventsDoc = JsonDocument.Parse(File.ReadAllText(eventsJson));
            Assert.False(eventsDoc.RootElement.TryGetProperty("preRollSeconds", out _),
                "Events sidecar should also omit preRollSeconds when no pre-roll attached.");
        }
    }

    /// <summary>
    /// Waits on TCSes because the controller reports a pre-roll death
    /// fire-and-forget from the process-exit thread.
    /// </summary>
    private sealed class HealthAwaitingObserver : IRecordingLifecycleObserver
    {
        public TaskCompletionSource<string> UnhealthyTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<(string Message, Exception? Ex)> ErrorTcs { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OnRecordingStartedAsync(RecordingMetadata m, CancellationToken ct) => Task.CompletedTask;
        public Task OnRecordingFinalizedAsync(ClipFinalization c, CancellationToken ct) => Task.CompletedTask;
        public Task OnRecordingErrorAsync(string message, Exception? ex, CancellationToken ct)
        { ErrorTcs.TrySetResult((message, ex)); return Task.CompletedTask; }
        public Task OnRecorderUnhealthyAsync(string message, CancellationToken ct)
        { UnhealthyTcs.TrySetResult(message); return Task.CompletedTask; }
        public Task OnRecorderHealthyAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task PreRoll_unexpected_exit_marks_recorder_unhealthy_without_combat_traffic()
    {
        using var dir = new TempDirectory();
        var preRoll = new StubPreRollRecorder(Path.Combine(dir.Path, ".preroll"));
        var observer = new HealthAwaitingObserver();
        var controller = new RecorderController(
            new EncounterDetector(), new StubRecorder(), new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            observer: observer,
            preRoll: preRoll);

        // The pre-roll process dies while the daemon is idle — e.g. gsr's
        // "display not found" at startup. No combat-log events flow, so the
        // warning must surface without the event loop ticking.
        preRoll.Raise(new RecorderExitedUnexpectedlyEventArgs(51, "gsr error: display \"DisplayPort-0\" not found"));

        var unhealthy = await observer.UnhealthyTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("51", unhealthy);
        Assert.Contains("display", unhealthy);

        // The per-clip error toast fires too, carrying the same stderr tail.
        var (errorMessage, _) = await observer.ErrorTcs.Task.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("Pre-roll recorder exited unexpectedly", errorMessage);

        await controller.DisposeAsync();
    }
}
