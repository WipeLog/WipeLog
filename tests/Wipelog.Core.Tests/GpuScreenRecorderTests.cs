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
using Xunit;

namespace Wipelog.Core.Tests;

public class GpuScreenRecorderTests
{
    private static string FakeScriptPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-gpu-screen-recorder.sh");
        // CopyToOutputDirectory does not preserve the executable bit, so set it here.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    private static GpuScreenRecorder NewRecorder(string? executablePath = null) =>
        new(
            new GpuScreenRecorderOptions(
                ExecutablePath: executablePath ?? FakeScriptPath(),
                // Skip the focused-mode autodetect (which would shell out to
                // `--list-monitors` and hit a 5s timeout against the fake
                // script). Real-world callers either supply this or rely on
                // the autodetect; tests use an explicit value.
                CaptureSize: "1920x1080"),
            NullLogger<GpuScreenRecorder>.Instance);

    [Fact]
    public async Task Start_then_stop_writes_output_and_returns_to_idle()
    {
        using var dir = new TempDirectory();
        var output = Path.Combine(dir.Path, "out.mp4");
        await using var rec = NewRecorder();

        await rec.StartAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal(RecorderState.Recording, rec.State);

        // Give the Python interpreter time to install its signal handlers before
        // we send SIGINT — without this, SIGINT can arrive before signal.signal()
        // runs and the default-action termination skips our trap.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await rec.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecorderState.Idle, rec.State);
        Assert.True(File.Exists(output), "Fake script should have written the output on SIGINT.");
        Assert.Equal("fake-recording", await File.ReadAllTextAsync(output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Stop_without_start_is_a_noop()
    {
        await using var rec = NewRecorder();
        await rec.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecorderState.Idle, rec.State);
    }

    [Fact]
    public async Task Double_start_throws()
    {
        using var dir = new TempDirectory();
        var output = Path.Combine(dir.Path, "out.mp4");
        await using var rec = NewRecorder();

        await rec.StartAsync(output, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rec.StartAsync(output, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_replay_throws_when_replay_buffer_not_configured()
    {
        using var dir = new TempDirectory();
        var output = Path.Combine(dir.Path, "out.mp4");
        await using var rec = NewRecorder();

        await rec.StartAsync(output, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => rec.SaveReplayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Save_replay_signals_subprocess_when_configured()
    {
        using var dir = new TempDirectory();
        var output = Path.Combine(dir.Path, "out.mp4");
        await using var rec = new GpuScreenRecorder(
            new GpuScreenRecorderOptions(
                ExecutablePath: FakeScriptPath(),
                ReplayBufferSeconds: 30,
                CaptureSize: "1920x1080"),
            NullLogger<GpuScreenRecorder>.Instance);

        await rec.StartAsync(output, TestContext.Current.CancellationToken);
        await rec.SaveReplayAsync(TestContext.Current.CancellationToken);
        // Without an external observation hook we just verify the recorder
        // is still running after the SIGUSR1 (the fake script does not exit).
        Assert.Equal(RecorderState.Recording, rec.State);
        await rec.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Missing_executable_throws_with_helpful_message()
    {
        var rec = new GpuScreenRecorder(
            new GpuScreenRecorderOptions(ExecutablePath: "/nonexistent/path/to/gpu-screen-recorder-xyz"),
            NullLogger<GpuScreenRecorder>.Instance);

        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => rec.StartAsync("/tmp/x", TestContext.Current.CancellationToken));
        Assert.Contains("gpu-screen-recorder", ex.Message);
        Assert.Contains("flatpak", ex.Message);
        await rec.DisposeAsync();
    }

    [Fact]
    public void Builds_argv_with_replay_flag_when_buffer_set()
    {
        var args = GpuScreenRecorder.BuildArgs("/tmp/out", new GpuScreenRecorderOptions(
            ReplayBufferSeconds: 30, Framerate: 30, Codec: "hevc")).ToList();

        Assert.Contains("-r", args);
        Assert.Equal("30", args[args.IndexOf("-r") + 1]);
        Assert.Contains("-k", args);
        Assert.Equal("hevc", args[args.IndexOf("-k") + 1]);
        Assert.Contains("-o", args);
        Assert.Equal("/tmp/out", args[args.IndexOf("-o") + 1]);
        Assert.Contains("-f", args);
        Assert.Equal("30", args[args.IndexOf("-f") + 1]);
    }

    [Fact]
    public void Builds_argv_without_replay_flag_when_buffer_null()
    {
        var args = GpuScreenRecorder.BuildArgs("/tmp/out", new GpuScreenRecorderOptions()).ToList();
        Assert.DoesNotContain("-r", args);
    }

    [Fact]
    public void Default_quality_is_medium()
    {
        var args = GpuScreenRecorder.BuildArgs("/tmp/out", new GpuScreenRecorderOptions()).ToList();
        Assert.Equal("medium", args[args.IndexOf("-q") + 1]);
    }

    [Fact]
    public void Build_args_emits_dash_s_when_capture_size_set_on_options()
    {
        var args = GpuScreenRecorder.BuildArgs("/tmp/out",
            new GpuScreenRecorderOptions(CaptureSize: "1920x1080")).ToList();
        Assert.Contains("-s", args);
        Assert.Equal("1920x1080", args[args.IndexOf("-s") + 1]);
    }

    [Fact]
    public void Build_args_emits_dash_s_when_resolved_capture_size_passed()
    {
        // Resolved size is what GpuScreenRecorder hands BuildArgs after asking
        // gsr for --list-monitors. Options.CaptureSize is null in that case.
        var args = GpuScreenRecorder.BuildArgs("/tmp/out",
            new GpuScreenRecorderOptions(), resolvedCaptureSize: "3840x2160").ToList();
        Assert.Contains("-s", args);
        Assert.Equal("3840x2160", args[args.IndexOf("-s") + 1]);
    }

    [Fact]
    public void Build_args_prefers_explicit_capture_size_over_resolved()
    {
        var args = GpuScreenRecorder.BuildArgs("/tmp/out",
            new GpuScreenRecorderOptions(CaptureSize: "1920x1080"),
            resolvedCaptureSize: "3840x2160").ToList();
        Assert.Equal("1920x1080", args[args.IndexOf("-s") + 1]);
    }

    [Fact]
    public void Build_args_does_not_emit_dash_s_when_neither_set()
    {
        var args = GpuScreenRecorder.BuildArgs("/tmp/out", new GpuScreenRecorderOptions()).ToList();
        Assert.DoesNotContain("-s", args);
    }

    [Fact]
    public void Custom_quality_is_emitted()
    {
        var args = GpuScreenRecorder.BuildArgs("/tmp/out",
            new GpuScreenRecorderOptions(Quality: "very_high")).ToList();
        Assert.Equal("very_high", args[args.IndexOf("-q") + 1]);
    }

    private static string FakeDyingScriptPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "fake-gpu-screen-recorder-dies.sh");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    [Fact]
    public void Monitor_list_parses_real_gsr_output()
    {
        // Exact format gpu-screen-recorder emits on a multi-monitor box.
        const string sample = "DisplayPort-0|2560x1440\nDisplayPort-1|3840x2160\n";
        var monitors = GpuScreenRecorderMonitorList.Parse(sample);

        Assert.Equal(2, monitors.Count);
        Assert.Equal("DisplayPort-0", monitors[0].Name);
        Assert.Equal(2560, monitors[0].Width);
        Assert.Equal(1440, monitors[0].Height);
        Assert.Equal("DisplayPort-1", monitors[1].Name);
        Assert.Equal(3840, monitors[1].Width);
        Assert.Equal(2160, monitors[1].Height);
    }

    [Fact]
    public void Monitor_list_skips_malformed_lines()
    {
        const string sample = """
            DisplayPort-0|2560x1440
            garbage
            HDMI-1|
            |800x600
            HDMI-2|x600
            HDMI-3|800x
            eDP-1|1920x1080
            """;
        var monitors = GpuScreenRecorderMonitorList.Parse(sample);

        Assert.Equal(2, monitors.Count);
        Assert.Equal("DisplayPort-0", monitors[0].Name);
        Assert.Equal("eDP-1", monitors[1].Name);
    }

    [Fact]
    public void Pick_largest_chooses_by_pixel_area()
    {
        var monitors = new[]
        {
            new GpuScreenRecorderMonitorList.Monitor("DisplayPort-0", 2560, 1440),
            new GpuScreenRecorderMonitorList.Monitor("DisplayPort-1", 3840, 2160),
        };
        var pick = GpuScreenRecorderMonitorList.PickLargest(monitors);
        Assert.NotNull(pick);
        Assert.Equal("DisplayPort-1", pick!.Name);
    }

    [Fact]
    public void Pick_largest_returns_null_on_empty_list()
    {
        Assert.Null(GpuScreenRecorderMonitorList.PickLargest(Array.Empty<GpuScreenRecorderMonitorList.Monitor>()));
    }

    [Fact]
    public void Pick_largest_breaks_ties_by_name_for_determinism()
    {
        var monitors = new[]
        {
            new GpuScreenRecorderMonitorList.Monitor("eDP-1", 1920, 1080),
            new GpuScreenRecorderMonitorList.Monitor("DisplayPort-0", 1920, 1080),
        };
        var pick = GpuScreenRecorderMonitorList.PickLargest(monitors);
        Assert.Equal("DisplayPort-0", pick!.Name);
    }

    [Fact]
    public async Task Process_death_during_recording_raises_exited_unexpectedly_with_stderr_tail()
    {
        using var dir = new TempDirectory();
        var output = Path.Combine(dir.Path, "out.mp4");
        await using var rec = NewRecorder(FakeDyingScriptPath());

        var tcs = new TaskCompletionSource<RecorderExitedUnexpectedlyEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        rec.ExitedUnexpectedly += (_, args) => tcs.TrySetResult(args);

        await rec.StartAsync(output, TestContext.Current.CancellationToken);

        // The fake script sleeps ~50ms then exits with code 7. Allow up to 5s
        // to absorb CI jitter without locking the test thread tightly.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var args = await tcs.Task.WaitAsync(timeout.Token);

        Assert.Equal(7, args.ExitCode);
        Assert.Contains("simulated capture failure", args.StderrTail);
        Assert.Contains("focused window", args.StderrTail);
        Assert.Equal(RecorderState.Failed, rec.State);

        // After unexpected death, StopAsync should clean up the handle and
        // return idle without throwing.
        await rec.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecorderState.Idle, rec.State);
    }
}
