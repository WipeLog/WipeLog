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

public class PreRollRecorderTests
{
    private static string FakeReplayScriptPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "fake-gpu-screen-recorder-replay.sh");
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return path;
    }

    private static (GpuScreenRecorder Inner, PreRollRecorder PreRoll) NewPair(string outputDir)
    {
        var inner = new GpuScreenRecorder(
            new GpuScreenRecorderOptions(
                ExecutablePath: FakeReplayScriptPath(),
                ReplayBufferSeconds: 30,
                CaptureSize: "1920x1080"),
            NullLogger<GpuScreenRecorder>.Instance);
        var preRoll = new PreRollRecorder(
            inner, outputDir, NullLogger<PreRollRecorder>.Instance,
            saveTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50));
        return (inner, preRoll);
    }

    [Fact]
    public async Task Start_creates_output_directory_and_runs_subprocess()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var (_, preRoll) = NewPair(preRollDir);
        await using var _ = preRoll;

        await preRoll.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(preRollDir));
        Assert.Equal(RecorderState.Recording, preRoll.State);

        await preRoll.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecorderState.Idle, preRoll.State);
    }

    [Fact]
    public async Task Save_returns_path_of_new_file_produced_by_sigusr1()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var (_, preRoll) = NewPair(preRollDir);
        await using var _ = preRoll;

        await preRoll.StartAsync(TestContext.Current.CancellationToken);

        // Same wait-for-signal-handler dance as GpuScreenRecorderTests: SIGUSR1
        // delivered before the Python handler is installed would be lost.
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var saved = await preRoll.SaveAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(saved);
        Assert.True(File.Exists(saved));
        Assert.StartsWith(preRollDir, saved);
        Assert.EndsWith(".mp4", saved);
        // Fake script writes a deterministic payload across two flushes.
        var bytes = await File.ReadAllBytesAsync(saved!, TestContext.Current.CancellationToken);
        Assert.Equal("fake-preroll-part-1-and-part-2", System.Text.Encoding.UTF8.GetString(bytes));

        await preRoll.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Multiple_saves_return_distinct_files()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var (_, preRoll) = NewPair(preRollDir);
        await using var _ = preRoll;

        await preRoll.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(300, TestContext.Current.CancellationToken);

        var first = await preRoll.SaveAsync(TestContext.Current.CancellationToken);
        var second = await preRoll.SaveAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));

        await preRoll.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Save_returns_null_when_not_running()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var (_, preRoll) = NewPair(preRollDir);
        await using var _ = preRoll;

        var result = await preRoll.SaveAsync(TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task Start_is_idempotent_when_already_recording()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var (_, preRoll) = NewPair(preRollDir);
        await using var _ = preRoll;

        await preRoll.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecorderState.Recording, preRoll.State);

        // Second call should be a no-op and not throw.
        await preRoll.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RecorderState.Recording, preRoll.State);

        await preRoll.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Exited_unexpectedly_event_propagates_from_wrapped_recorder()
    {
        using var dir = new TempDirectory();
        var preRollDir = Path.Combine(dir.Path, "preroll");
        var stub = new StubRecorder();
        await using var preRoll = new PreRollRecorder(
            stub, preRollDir, NullLogger<PreRollRecorder>.Instance);

        RecorderExitedUnexpectedlyEventArgs? received = null;
        preRoll.ExitedUnexpectedly += (_, args) => received = args;

        stub.RaiseExitedUnexpectedly(exitCode: 7, stderrTail: "boom");

        Assert.NotNull(received);
        Assert.Equal(7, received!.ExitCode);
        Assert.Equal("boom", received.StderrTail);
    }
}
