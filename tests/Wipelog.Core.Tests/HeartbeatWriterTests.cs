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
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.Detection;
using Wipelog.Core.Status;
using Wipelog.Daemon.Status;
using Xunit;

namespace Wipelog.Core.Tests;

public class HeartbeatWriterTests
{
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    [Fact]
    public async Task ExecuteAsync_writes_status_file_periodically()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var state = new HeartbeatState(clock);
        var writer = new HeartbeatWriter(state, NullLogger<HeartbeatWriter>.Instance, path);

        using var cts = new CancellationTokenSource();
        var run = writer.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.True(File.Exists(path), "Heartbeat file was not written within 5s.");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var root = doc.RootElement;
        Assert.Equal("Idle", root.GetProperty("state").GetString());
        Assert.True(root.GetProperty("pid").GetInt32() > 0);

        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Status_reflects_recording_state_changes()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var state = new HeartbeatState(clock);
        var writer = new HeartbeatWriter(state, NullLogger<HeartbeatWriter>.Instance, path);

        using var cts = new CancellationTokenSource();
        var run = writer.StartAsync(cts.Token);

        state.MarkRecordingStarted(new RecordingMetadata(
            ContentType.MythicPlus, "Ara-Kara, City of Echoes",
            DifficultyId: null, KeystoneLevel: 18, EncounterId: null,
            InstanceId: 503, StartedAt: clock.Now));

        // Wait for the next periodic write to land.
        DaemonStatus? read = null;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    read = JsonSerializer.Deserialize<DaemonStatus>(
                        await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken),
                        ReadOpts);
                    if (read?.State == DaemonState.Recording) break;
                }
                catch (JsonException) { /* concurrent partial write — retry */ }
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.NotNull(read);
        Assert.Equal(DaemonState.Recording, read!.State);
        Assert.NotNull(read.CurrentRecording);
        Assert.Equal("Ara-Kara, City of Echoes", read.CurrentRecording!.DisplayName);
        Assert.Equal(18, read.CurrentRecording.KeystoneLevel);

        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await writer.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_deletes_the_status_file()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var state = new HeartbeatState(clock);
        var writer = new HeartbeatWriter(state, NullLogger<HeartbeatWriter>.Instance, path);

        using var cts = new CancellationTokenSource();
        var run = writer.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.True(File.Exists(path));

        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
        await writer.StopAsync(CancellationToken.None);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void HeartbeatState_clears_last_error_after_retention()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var state = new HeartbeatState(clock);
        state.MarkError("boom");

        var fresh = state.Snapshot();
        Assert.Equal("boom", fresh.LastError);

        clock.Now = clock.Now.AddMinutes(6);
        var stale = state.Snapshot();
        Assert.Null(stale.LastError);
    }

    private sealed class NoopNotifications : Wipelog.Daemon.Notifications.INotificationService
    {
        public Task NotifyClipSavedAsync(Wipelog.Core.Recording.ClipFinalization clip, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyErrorAsync(string title, string message, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDiskWarningAsync(long freeGb, long minFreeGb, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyAutoDeleteAsync(int deletedCount, long bytesFreed, CancellationToken ct) => Task.CompletedTask;
        public Task NotifyDiskFullFavoritesOnlyAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task DaemonLifecycleObserver_sets_and_clears_recorder_error()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var state = new HeartbeatState(clock);
        var observer = new DaemonLifecycleObserver(state, new NoopNotifications());

        await observer.OnRecorderUnhealthyAsync("display not found", CancellationToken.None);
        Assert.Equal("display not found", state.Snapshot().RecorderError);

        await observer.OnRecorderHealthyAsync(CancellationToken.None);
        Assert.Null(state.Snapshot().RecorderError);
    }

    [Fact]
    public void HeartbeatState_recorder_error_is_sticky_until_cleared()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var state = new HeartbeatState(clock);
        Assert.Null(state.Snapshot().RecorderError);

        state.SetRecorderError("gsr exited with code 51");
        Assert.Equal("gsr exited with code 51", state.Snapshot().RecorderError);

        // Unlike LastError there is no retention window — a broken recorder
        // stays flagged across an idle night until capture provably works.
        clock.Now = clock.Now.AddHours(12);
        Assert.Equal("gsr exited with code 51", state.Snapshot().RecorderError);

        state.SetRecorderError(null);
        Assert.Null(state.Snapshot().RecorderError);
    }

    [Fact]
    public void HeartbeatState_increments_clip_count_on_finalize()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        var state = new HeartbeatState(clock);
        Assert.Equal(0, state.Snapshot().ClipsRecordedThisSession);
        state.MarkRecordingFinalized();
        state.MarkRecordingFinalized();
        Assert.Equal(2, state.Snapshot().ClipsRecordedThisSession);
    }
}
