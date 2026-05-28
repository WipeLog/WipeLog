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

using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecorderControllerTests
{
    private static DateTimeOffset T(int hour, int minute = 0, int second = 0, int millis = 0) =>
        new(2026, 5, 9, hour, minute, second, millis, TimeSpan.Zero);

    private static async Task<(StubRecorder Recorder, TempDirectory Dir, List<string> Files)> RunAsync(
        params CombatLogEvent[] events)
    {
        var dir = new TempDirectory();
        var stub = new StubRecorder();
        var detector = new EncounterDetector();
        var controller = new RecorderController(
            detector, stub, new DefaultClipNamingStrategy(), dir.Path,
            NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        foreach (var e in events)
        {
            await channel.Writer.WriteAsync(e);
        }
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, CancellationToken.None);
        return (stub, dir, Directory.GetFiles(dir.Path).OrderBy(f => f).ToList());
    }

    [Fact]
    public async Task Raid_kill_writes_video_and_sidecar_with_kill_filename()
    {
        var (stub, dir, files) = await RunAsync(
            new EncounterStart(T(19, 43, 1, 122), 2902, "Ulgrax the Devourer", 16, 20, 2657),
            new EncounterEnd(T(19, 47, 55, 890), 2902, "Ulgrax the Devourer", 16, 20, Success: true));
        using (dir)
        {
            Assert.Single(stub.StartedPaths);
            Assert.Equal(1, stub.StopCount);

            var mp4 = files.Single(f => f.EndsWith(".mp4", StringComparison.Ordinal));
            var json = files.Single(f => f.EndsWith(".json", StringComparison.Ordinal)
                                          && !f.EndsWith(".events.json", StringComparison.Ordinal));

            Assert.EndsWith("2026-05-09_19-43-01_ulgrax-the-devourer_mythic_kill.mp4", mp4);
            Assert.Equal("fake video", File.ReadAllText(mp4));

            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var root = doc.RootElement;
            Assert.Equal("RaidEncounter", root.GetProperty("contentType").GetString());
            Assert.Equal("Ulgrax the Devourer", root.GetProperty("displayName").GetString());
            Assert.Equal("Mythic", root.GetProperty("difficulty").GetString());
            Assert.Equal(2902, root.GetProperty("encounterId").GetInt32());
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(294, root.GetProperty("durationSeconds").GetInt32());
            Assert.Equal(Path.GetFileName(mp4), root.GetProperty("videoFile").GetString());
            Assert.Equal(2, root.GetProperty("version").GetInt32());
        }
    }

    [Fact]
    public async Task Mythic_plus_timed_writes_sidecar_with_keystone_and_no_difficulty()
    {
        var (_, dir, files) = await RunAsync(
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135, 136, 148 }),
            new ChallengeModeEnd(T(20, 43, 21, 500), 2290, Success: true, KeystoneLevel: 12, Duration: TimeSpan.FromMilliseconds(1701500)));
        using (dir)
        {
            var mp4 = files.Single(f => f.EndsWith(".mp4", StringComparison.Ordinal));
            var json = files.Single(f => f.EndsWith(".json", StringComparison.Ordinal)
                                          && !f.EndsWith(".events.json", StringComparison.Ordinal));
            Assert.EndsWith("2026-05-09_20-15-00_the-stonevault_+12_timed.mp4", mp4);

            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var root = doc.RootElement;
            Assert.Equal("MythicPlus", root.GetProperty("contentType").GetString());
            Assert.Equal(12, root.GetProperty("keystoneLevel").GetInt32());
            Assert.False(root.TryGetProperty("difficulty", out _));
            Assert.True(root.GetProperty("success").GetBoolean());
        }
    }

    [Fact]
    public async Task Wipe_uses_wipe_filename()
    {
        var (_, dir, files) = await RunAsync(
            new EncounterStart(T(19, 0, 0), 2902, "Boss", 16, 20, 2657),
            new EncounterEnd(T(19, 5, 0), 2902, "Boss", 16, 20, Success: false));
        using (dir)
        {
            var mp4 = files.Single(f => f.EndsWith(".mp4", StringComparison.Ordinal));
            Assert.EndsWith("_wipe.mp4", mp4);
        }
    }

    [Fact]
    public async Task Temp_file_does_not_appear_in_output_listing_after_finalization()
    {
        var (_, dir, files) = await RunAsync(
            new EncounterStart(T(19, 0, 0), 2902, "Boss", 16, 20, 2657),
            new EncounterEnd(T(19, 5, 0), 2902, "Boss", 16, 20, Success: true));
        using (dir)
        {
            Assert.DoesNotContain(files, f => f.Contains(".recording.mp4", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Concurrent_recording_request_stops_previous()
    {
        // First Encounter START, then a second START before END arrives.
        // Detector won't naturally produce overlapping starts (it requires Idle),
        // so we hit the controller directly via channel sequencing that includes a
        // CombatLogVersion mid-stream, which triggers a synthetic stop, and then
        // a fresh start.
        var (stub, dir, files) = await RunAsync(
            new EncounterStart(T(19, 0, 0), 2902, "First", 16, 20, 2657),
            new CombatLogVersion(T(19, 0, 30), 22, true),
            new EncounterStart(T(19, 1, 0), 2903, "Second", 16, 20, 2657),
            new EncounterEnd(T(19, 5, 0), 2903, "Second", 16, 20, Success: true));
        using (dir)
        {
            Assert.Equal(2, stub.StartedPaths.Count);
            Assert.Equal(2, stub.StopCount);
            Assert.Equal(2, files.Count(f => f.EndsWith(".mp4", StringComparison.Ordinal)));
            Assert.Equal(2, files.Count(f => f.EndsWith(".json", StringComparison.Ordinal)
                                              && !f.EndsWith(".events.json", StringComparison.Ordinal)));
            Assert.Equal(2, files.Count(f => f.EndsWith(".events.json", StringComparison.Ordinal)));
            // The first encounter ended without a real END event, so its file
            // gets a wipe suffix; the second is a clean kill.
            Assert.Contains(files, f => f.EndsWith("_wipe.mp4", StringComparison.Ordinal));
            Assert.Contains(files, f => f.EndsWith("_kill.mp4", StringComparison.Ordinal));
        }
    }

    private sealed class CapturingObserver : IRecordingLifecycleObserver
    {
        public List<RecordingMetadata> Started { get; } = new();
        public List<ClipFinalization> Finalized { get; } = new();
        public List<(string Message, Exception? Ex)> Errors { get; } = new();

        public Task OnRecordingStartedAsync(RecordingMetadata m, CancellationToken ct)
        { Started.Add(m); return Task.CompletedTask; }
        public Task OnRecordingFinalizedAsync(ClipFinalization c, CancellationToken ct)
        { Finalized.Add(c); return Task.CompletedTask; }
        public Task OnRecordingErrorAsync(string message, Exception? ex, CancellationToken ct)
        { Errors.Add((message, ex)); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Unexpected_recorder_exit_mid_recording_notifies_and_finalizes_as_failure()
    {
        using var dir = new TempDirectory();
        StubRecorder? stub = null;
        stub = new StubRecorder
        {
            OnStart = async outputPath =>
            {
                // Simulate a partial recording on disk, then the process
                // dying out from under us before the encounter ends.
                await File.WriteAllTextAsync(outputPath, "partial video");
                stub!.RaiseExitedUnexpectedly(exitCode: 139, stderrTail: "Segmentation fault");
            },
        };
        var observer = new CapturingObserver();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            observer: observer);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 0, 0), 2902, "Boss", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        // An innocuous follow-up event so the loop iterates once more and
        // picks up the latched unexpected-exit signal.
        await channel.Writer.WriteAsync(
            new CombatLogVersion(T(19, 0, 1), 22, true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();
        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        Assert.Single(observer.Started);
        Assert.Single(observer.Errors);
        Assert.Contains("139", observer.Errors[0].Message);
        Assert.Contains("Segmentation fault", observer.Errors[0].Message);

        // The partial bytes on disk are still preserved as a failure clip.
        var files = Directory.GetFiles(dir.Path).OrderBy(f => f).ToList();
        Assert.Contains(files, f => f.EndsWith("_wipe.mp4", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.Contains(".recording.mp4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_temp_file_at_finalize_notifies_error()
    {
        using var dir = new TempDirectory();
        // Stub that pretends to start but writes no file — mirrors a recorder
        // process that crashed and unlinked its output before stop.
        var stub = new StubRecorder { OnStart = _ => Task.CompletedTask };
        var observer = new CapturingObserver();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            observer: observer);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 0, 0), 2902, "Boss", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(19, 5, 0), 2902, "Boss", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();
        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        Assert.Single(observer.Started);
        Assert.Empty(observer.Finalized);
        Assert.Single(observer.Errors);
        Assert.Contains("temp file", observer.Errors[0].Message);
        // No clip, no sidecar — but also nothing left lingering on disk.
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task Sidecar_handles_collision_with_dash_suffix()
    {
        using var dir = new TempDirectory();
        // Pre-populate a colliding file so the strategy must pick a -1 suffix.
        File.WriteAllText(
            Path.Combine(dir.Path, "2026-05-09_19-43-01_ulgrax-the-devourer_mythic_kill.mp4"),
            "old");
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var ch = Channel.CreateUnbounded<CombatLogEvent>();
        await ch.Writer.WriteAsync(new EncounterStart(T(19, 43, 1, 122), 2902, "Ulgrax the Devourer", 16, 20, 2657), TestContext.Current.CancellationToken);
        await ch.Writer.WriteAsync(new EncounterEnd(T(19, 47, 55, 890), 2902, "Ulgrax the Devourer", 16, 20, Success: true), TestContext.Current.CancellationToken);
        ch.Writer.Complete();
        await controller.RunAsync(ch.Reader, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(Path.Combine(dir.Path, "2026-05-09_19-43-01_ulgrax-the-devourer_mythic_kill-1.mp4")));
    }
}
