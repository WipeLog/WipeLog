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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wipelog.App.Services;
using Wipelog.Core.Status;
using Xunit;

namespace Wipelog.Core.Tests;

public class FileBasedDaemonStatusReaderTests
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private static void Write(string path, DaemonStatus status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(status, WriteOpts));
    }

    private static DaemonStatus Sample(DateTimeOffset heartbeatAt, DaemonState state = DaemonState.Idle) => new(
        Version: "1.0.0",
        Pid: 1234,
        StartedAt: heartbeatAt.AddMinutes(-5),
        HeartbeatAt: heartbeatAt,
        State: state,
        CurrentRecording: null,
        ClipsRecordedThisSession: 0,
        LastError: null);

    [Fact]
    public void ReadOnce_returns_null_when_file_missing()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        using var reader = new FileBasedDaemonStatusReader(path, clock);
        Assert.Null(reader.ReadOnce());
        Assert.Null(reader.Current);
    }

    [Fact]
    public void ReadOnce_returns_status_when_file_is_fresh()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var now = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now);
        Write(path, Sample(heartbeatAt: now.AddSeconds(-3)));

        using var reader = new FileBasedDaemonStatusReader(path, clock);
        var read = reader.ReadOnce();
        Assert.NotNull(read);
        Assert.Equal(DaemonState.Idle, read!.State);
        Assert.Equal(1234, read.Pid);
    }

    [Fact]
    public void ReadOnce_returns_null_when_heartbeat_is_stale()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var now = new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(now);
        Write(path, Sample(heartbeatAt: now.AddSeconds(-15)));

        using var reader = new FileBasedDaemonStatusReader(path, clock);
        Assert.Null(reader.ReadOnce());
    }

    [Fact]
    public void ReadOnce_treats_malformed_json_as_unchanged()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));

        // First write a real status; reader should pick it up.
        Write(path, Sample(heartbeatAt: clock.Now));
        using var reader = new FileBasedDaemonStatusReader(path, clock);
        Assert.NotNull(reader.ReadOnce());

        // Then write garbage — the reader should retain the previous value
        // rather than reporting offline on a transient partial write.
        File.WriteAllText(path, "{ this is not valid json");
        var after = reader.ReadOnce();
        Assert.NotNull(after);
        Assert.Equal(1234, after!.Pid);
    }

    [Fact]
    public async Task StatusChanged_fires_when_state_transitions()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "status.json");
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 11, 12, 0, 0, TimeSpan.Zero));
        Write(path, Sample(heartbeatAt: clock.Now, state: DaemonState.Idle));

        using var reader = new FileBasedDaemonStatusReader(path, clock);
        reader.ReadOnce(); // prime

        DaemonStatus? observed = null;
        var signal = new TaskCompletionSource<DaemonStatus?>();
        reader.StatusChanged += (_, s) => { observed = s; signal.TrySetResult(s); };

        Write(path, Sample(heartbeatAt: clock.Now, state: DaemonState.Recording));
        reader.ReadOnce();

        await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.True(signal.Task.IsCompleted);
        Assert.NotNull(observed);
        Assert.Equal(DaemonState.Recording, observed!.State);
    }
}
