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

using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecorderControllerStopTests
{
    private static DateTimeOffset T(int hour, int minute = 0, int second = 0) =>
        new(2026, 5, 9, hour, minute, second, TimeSpan.Zero);

    /// <summary>
    /// Polls until <paramref name="predicate"/> returns true or the timeout
    /// fires. Tests need to wait for the background event loop to have
    /// observed the StartRecording before the abort can do anything.
    /// </summary>
    private static async Task WaitFor(Func<bool> predicate, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.True(predicate(), "Timed out waiting for condition.");
    }

    [Fact]
    public async Task Abort_returns_false_when_no_recording_is_active()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var aborted = await controller.StopCurrentAsync(TestContext.Current.CancellationToken);
        Assert.False(aborted);
        Assert.Equal(0, stub.StopCount);

        channel.Writer.Complete();
        await runTask;
    }

    [Fact]
    public async Task Abort_finalises_active_recording_as_failure()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        // Trigger a raid encounter start; the controller should pick it up and
        // call recorder.StartAsync.
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await WaitFor(() => stub.StartedPaths.Count == 1);

        var aborted = await controller.StopCurrentAsync(TestContext.Current.CancellationToken);
        Assert.True(aborted);

        channel.Writer.Complete();
        await runTask;

        // The recorder must have been stopped exactly once and a wipe-suffix
        // mp4 must exist with a matching sidecar marked success=false.
        Assert.Equal(1, stub.StopCount);
        var mp4 = Directory.GetFiles(dir.Path, "*.mp4").Single();
        Assert.EndsWith("_wipe.mp4", mp4);
        var jsonPath = Directory.GetFiles(dir.Path, "*.json")
            .Single(f => !f.EndsWith(".events.json", StringComparison.Ordinal));
        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Manual_recording_starts_and_stops_with_success_true()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var started = await controller.StartManualAsync(
            new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        Assert.True(started);
        Assert.Single(stub.StartedPaths);

        var stopped = await controller.StopCurrentAsync(TestContext.Current.CancellationToken);
        Assert.True(stopped);

        channel.Writer.Complete();
        await runTask;

        // Manual stop is intentional, so the clip finalises as success=true
        // and uses the generic "_ok" suffix (manual content type falls into
        // the default branch of SynthesizeStop).
        var mp4 = Directory.GetFiles(dir.Path, "*.mp4").Single();
        Assert.EndsWith("_manual_ok.mp4", mp4);
        var jsonPath = Directory.GetFiles(dir.Path, "*.json")
            .Single(f => !f.EndsWith(".events.json", StringComparison.Ordinal));
        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Manual", doc.RootElement.GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task Manual_start_refused_while_another_recording_is_active()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await WaitFor(() => stub.StartedPaths.Count == 1);

        var started = await controller.StartManualAsync(
            DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        Assert.False(started);
        Assert.Single(stub.StartedPaths);   // unchanged

        channel.Writer.Complete();
        await runTask;
    }

    [Fact]
    public async Task Abort_called_twice_on_same_clip_is_idempotent()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await WaitFor(() => stub.StartedPaths.Count == 1);

        var first = await controller.StopCurrentAsync(TestContext.Current.CancellationToken);
        var second = await controller.StopCurrentAsync(TestContext.Current.CancellationToken);
        Assert.True(first);
        Assert.False(second);   // already finalised; nothing to abort

        channel.Writer.Complete();
        await runTask;
        // Only one file pair; the second abort didn't finalise anything.
        Assert.Single(Directory.GetFiles(dir.Path, "*.mp4"));
    }
}
