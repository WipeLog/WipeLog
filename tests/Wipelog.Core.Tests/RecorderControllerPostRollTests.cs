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

public class RecorderControllerPostRollTests
{
    private static DateTimeOffset T(int hour, int minute = 0, int second = 0) =>
        new(2026, 5, 9, hour, minute, second, TimeSpan.Zero);

    [Fact]
    public async Task Stop_is_delayed_by_post_roll_seconds_before_calling_recorder_stop()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            postRollSeconds: 1);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        var sw = Stopwatch.StartNew();
        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.Equal(1, stub.StopCount);
        // 1s post-roll: allow a generous lower bound to absorb timer slop on
        // busy CI runners while still catching a "delay was skipped" regression.
        Assert.True(sw.Elapsed.TotalMilliseconds >= 800,
            $"Expected post-roll delay >= 800ms, got {sw.Elapsed.TotalMilliseconds:F0}ms.");
    }

    [Fact]
    public async Task Sidecar_records_post_roll_seconds_on_successful_concat()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            postRollSeconds: 1);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var jsonPath = Directory.GetFiles(dir.Path, "*.json")
            .Single(f => !f.EndsWith(".events.json", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.True(doc.RootElement.TryGetProperty("postRollSeconds", out var postRoll));
        Assert.Equal(1, postRoll.GetInt32());
    }

    [Fact]
    public async Task Zero_post_roll_omits_the_field_from_sidecar()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            postRollSeconds: 0);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var jsonPath = Directory.GetFiles(dir.Path, "*.json")
            .Single(f => !f.EndsWith(".events.json", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
        Assert.False(doc.RootElement.TryGetProperty("postRollSeconds", out _));
    }

    [Fact]
    public async Task Manual_stop_skips_the_post_roll_delay()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        // 5s post-roll would dominate the test runtime if it leaked into the
        // manual-stop path; the assertion is that the test completes quickly.
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            postRollSeconds: 5);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var started = await controller.StartManualAsync(
            new DateTimeOffset(2026, 5, 19, 14, 30, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        Assert.True(started);

        var sw = Stopwatch.StartNew();
        var stopped = await controller.StopCurrentAsync(TestContext.Current.CancellationToken);
        sw.Stop();
        Assert.True(stopped);

        channel.Writer.Complete();
        await runTask;

        // Manual stops go through SafeStopAsync, which never touches the
        // post-roll path. Anything under 1s confirms the 5s delay was skipped.
        Assert.True(sw.Elapsed.TotalSeconds < 1,
            $"Manual stop should not wait for post-roll; took {sw.Elapsed.TotalSeconds:F2}s.");
    }
}
