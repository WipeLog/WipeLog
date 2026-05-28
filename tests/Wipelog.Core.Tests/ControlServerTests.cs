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

using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Control;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Wipelog.Daemon.Control;
using Xunit;

namespace Wipelog.Core.Tests;

public class ControlServerTests
{
    private static DateTimeOffset T(int hour, int minute = 0, int second = 0) =>
        new(2026, 5, 9, hour, minute, second, TimeSpan.Zero);

    /// <summary>
    /// Override <c>XDG_RUNTIME_DIR</c> for the duration of the test so each
    /// run binds to a fresh path under a TempDirectory. Without this two
    /// tests running back-to-back would share the same per-user socket.
    /// </summary>
    private sealed class XdgOverride : IDisposable
    {
        private readonly string? _prev;
        public XdgOverride(string path)
        {
            _prev = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", path);
        }
        public void Dispose() => Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", _prev);
    }

    private static async Task WaitFor(Func<bool> predicate, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.True(predicate(), "Timed out waiting for condition.");
    }

    private static async Task<string> RoundTripAsync(string socketPath, string requestJson, CancellationToken ct)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
        await socket.SendAsync(Encoding.UTF8.GetBytes(requestJson), SocketFlags.None, ct);
        socket.Shutdown(SocketShutdown.Send);
        using var ms = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var n = await socket.ReceiveAsync(buffer, SocketFlags.None, ct);
            if (n <= 0) break;
            ms.Write(buffer, 0, n);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [Fact]
    public async Task Abort_command_returns_failure_when_no_recording_active()
    {
        using var dir = new TempDirectory();
        using var _ = new XdgOverride(dir.Path);
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        // The controller has no event loop running here; AbortCurrentAsync
        // still works because it operates on the shared state directly.
        var server = new ControlServer(controller, NullLogger<ControlServer>.Instance);
        var serverTask = server.StartAsync(TestContext.Current.CancellationToken);
        await serverTask;
        await WaitFor(() => server.SocketPath is not null && File.Exists(server.SocketPath));

        var json = await RoundTripAsync(server.SocketPath!,
            """{"command":"stopRecording"}""",
            TestContext.Current.CancellationToken);
        var resp = JsonSerializer.Deserialize<ControlResponse>(json, ControlProtocolJson.Options);

        Assert.NotNull(resp);
        Assert.False(resp!.Ok);
        Assert.Equal("idle", resp.State);
        Assert.NotNull(resp.Error);

        await server.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Abort_command_finalises_active_recording_and_returns_success()
    {
        using var dir = new TempDirectory();
        using var _ = new XdgOverride(dir.Path);
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var server = new ControlServer(controller, NullLogger<ControlServer>.Instance);
        await server.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => server.SocketPath is not null && File.Exists(server.SocketPath));

        await channel.Writer.WriteAsync(
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await WaitFor(() => stub.StartedPaths.Count == 1);

        var json = await RoundTripAsync(server.SocketPath!,
            """{"command":"stopRecording"}""",
            TestContext.Current.CancellationToken);
        var resp = JsonSerializer.Deserialize<ControlResponse>(json, ControlProtocolJson.Options);

        Assert.NotNull(resp);
        Assert.True(resp!.Ok);
        Assert.Equal("idle", resp.State);
        Assert.Equal(1, stub.StopCount);

        channel.Writer.Complete();
        await runTask;
        await server.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartManualRecording_command_starts_a_manual_clip()
    {
        using var dir = new TempDirectory();
        using var _ = new XdgOverride(dir.Path);
        var stub = new StubRecorder();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        var runTask = controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        var server = new ControlServer(controller, NullLogger<ControlServer>.Instance);
        await server.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => server.SocketPath is not null && File.Exists(server.SocketPath));

        var json = await RoundTripAsync(server.SocketPath!,
            """{"command":"startManualRecording"}""",
            TestContext.Current.CancellationToken);
        var resp = JsonSerializer.Deserialize<ControlResponse>(json, ControlProtocolJson.Options);

        Assert.NotNull(resp);
        Assert.True(resp!.Ok);
        Assert.Equal("recording", resp.State);
        Assert.Single(stub.StartedPaths);
        // Stem path embeds the "_manual" tag.
        Assert.Contains("_manual", stub.StartedPaths[0]);

        // A second StartManual while already recording is refused.
        var dupJson = await RoundTripAsync(server.SocketPath!,
            """{"command":"startManualRecording"}""",
            TestContext.Current.CancellationToken);
        var dupResp = JsonSerializer.Deserialize<ControlResponse>(dupJson, ControlProtocolJson.Options);
        Assert.NotNull(dupResp);
        Assert.False(dupResp!.Ok);

        channel.Writer.Complete();
        await runTask;
        await server.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Unknown_command_returns_failure_with_descriptive_error()
    {
        using var dir = new TempDirectory();
        using var _ = new XdgOverride(dir.Path);
        var controller = new RecorderController(
            new EncounterDetector(), new StubRecorder(), new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);
        var server = new ControlServer(controller, NullLogger<ControlServer>.Instance);
        await server.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => server.SocketPath is not null && File.Exists(server.SocketPath));

        var json = await RoundTripAsync(server.SocketPath!,
            """{"command":"fly"}""",
            TestContext.Current.CancellationToken);
        var resp = JsonSerializer.Deserialize<ControlResponse>(json, ControlProtocolJson.Options);

        Assert.NotNull(resp);
        Assert.False(resp!.Ok);
        Assert.Contains("unknown command", resp.Error, StringComparison.Ordinal);

        await server.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Stale_socket_file_from_a_previous_run_is_replaced_on_bind()
    {
        using var dir = new TempDirectory();
        using var _ = new XdgOverride(dir.Path);

        // Create a phantom socket file at the resolved path before the server
        // ever runs. The server must unlink it cleanly — otherwise bind()
        // fails with EADDRINUSE and the test catches the regression.
        var wipelogDir = Path.Combine(dir.Path, "wipelog");
        Directory.CreateDirectory(wipelogDir);
        var stalePath = Path.Combine(wipelogDir, ControlSocketPath.SocketFileName);
        File.WriteAllText(stalePath, "stale");

        var controller = new RecorderController(
            new EncounterDetector(), new StubRecorder(), new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance);
        var server = new ControlServer(controller, NullLogger<ControlServer>.Instance);
        await server.StartAsync(TestContext.Current.CancellationToken);
        await WaitFor(() => server.SocketPath is not null && File.Exists(server.SocketPath));

        // The bind succeeded, so we can talk to it.
        var json = await RoundTripAsync(server.SocketPath!,
            """{"command":"stopRecording"}""",
            TestContext.Current.CancellationToken);
        Assert.NotEmpty(json);

        await server.StopAsync(TestContext.Current.CancellationToken);
    }
}
