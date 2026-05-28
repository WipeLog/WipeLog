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
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Control;

namespace Wipelog.App.Services;

/// <summary>
/// Sends one-shot control commands to the daemon over its Unix-domain socket.
/// Each call opens a fresh connection, writes a JSON request, reads the JSON
/// response, and closes — there's no long-lived session because the protocol
/// is request/response.
///
/// <para>
/// Errors fall into three buckets, all surfaced as <see cref="ControlCallResult"/>:
/// </para>
/// <list type="bullet">
///   <item><c>Unreachable</c> — connect threw (daemon offline or socket missing).</item>
///   <item><c>Failure</c> — daemon answered with <c>ok=false</c> (e.g. nothing to abort).</item>
///   <item><c>Success</c> — daemon answered with <c>ok=true</c>.</item>
/// </list>
/// </summary>
public interface IDaemonControlClient
{
    Task<ControlCallResult> StopRecordingAsync(CancellationToken ct);
    Task<ControlCallResult> StartManualRecordingAsync(CancellationToken ct);
}

public sealed record ControlCallResult(
    ControlCallStatus Status,
    string? State = null,
    string? Error = null);

public enum ControlCallStatus
{
    Success,
    Failure,
    Unreachable,
}

public sealed class DaemonControlClient : IDaemonControlClient
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<DaemonControlClient> _logger;

    public DaemonControlClient(ILogger<DaemonControlClient> logger)
    {
        _logger = logger;
    }

    public Task<ControlCallResult> StopRecordingAsync(CancellationToken ct) =>
        SendAsync(new ControlRequest(ControlCommands.StopRecording), ct);

    public Task<ControlCallResult> StartManualRecordingAsync(CancellationToken ct) =>
        SendAsync(new ControlRequest(ControlCommands.StartManualRecording), ct);

    private async Task<ControlCallResult> SendAsync(ControlRequest req, CancellationToken ct)
    {
        var path = ControlSocketPath.Resolve();
        if (!File.Exists(path))
        {
            return new ControlCallResult(ControlCallStatus.Unreachable,
                Error: "daemon control socket not found (daemon offline?)");
        }

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(ConnectTimeout);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), connectCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not connect to daemon control socket at {Path}", path);
            return new ControlCallResult(ControlCallStatus.Unreachable,
                Error: "daemon did not accept the connection");
        }

        await using var stream = new NetworkStream(socket, ownsSocket: false);
        var payload = JsonSerializer.SerializeToUtf8Bytes(req, ControlProtocolJson.Options);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        // Half-close the write side so the daemon's read-until-EOF loop
        // returns instead of waiting for the response timeout.
        try { socket.Shutdown(SocketShutdown.Send); } catch { /* best-effort */ }

        using var respCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        respCts.CancelAfter(ResponseTimeout);
        using var ms = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            int n;
            try
            {
                n = await stream.ReadAsync(buffer, respCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ControlCallResult(ControlCallStatus.Unreachable,
                    Error: "daemon did not respond in time");
            }
            if (n <= 0) break;
            ms.Write(buffer, 0, n);
            if (ms.Length > 4096) break; // defensive cap
        }

        ControlResponse? resp;
        try
        {
            resp = JsonSerializer.Deserialize<ControlResponse>(
                ms.ToArray(), ControlProtocolJson.Options);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse daemon response.");
            return new ControlCallResult(ControlCallStatus.Unreachable, Error: "malformed daemon response");
        }

        if (resp is null)
        {
            return new ControlCallResult(ControlCallStatus.Unreachable, Error: "empty daemon response");
        }
        return new ControlCallResult(
            resp.Ok ? ControlCallStatus.Success : ControlCallStatus.Failure,
            State: resp.State,
            Error: resp.Error);
    }
}
