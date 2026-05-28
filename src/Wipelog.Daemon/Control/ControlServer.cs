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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Control;
using Wipelog.Core.Recording;

namespace Wipelog.Daemon.Control;

/// <summary>
/// Hosted service that listens on a Unix-domain socket and dispatches GUI
/// control commands to the <see cref="RecorderController"/>. One connection =
/// one request + one response + close, so we don't need to track sessions.
///
/// <para>
/// The socket path is computed by <see cref="ControlSocketPath.Resolve"/> so
/// both ends derive it from <c>$XDG_RUNTIME_DIR</c> without configuration. A
/// stale socket file from a previous (crashed) daemon is unlinked at startup;
/// no attempt is made to detect a running peer because the daemon's process
/// model already assumes a single instance.
/// </para>
/// </summary>
public sealed class ControlServer : BackgroundService
{
    private readonly RecorderController _controller;
    private readonly ILogger<ControlServer> _logger;
    private Socket? _listener;
    private string? _socketPath;

    public ControlServer(RecorderController controller, ILogger<ControlServer> logger)
    {
        _controller = controller;
        _logger = logger;
    }

    /// <summary>Resolved path of the socket once <see cref="ExecuteAsync"/>
    /// has bound it. Exposed for tests.</summary>
    internal string? SocketPath => _socketPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _socketPath = ControlSocketPath.Resolve();
        try
        {
            Directory.CreateDirectory(ControlSocketPath.ResolveDirectory());
            // Stale socket from a previous crashed run blocks bind() with
            // EADDRINUSE — silently unlink it so restart isn't a manual step.
            if (File.Exists(_socketPath)) File.Delete(_socketPath);

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            _listener.Listen(8);
            _logger.LogInformation("Control socket listening at {Path}", _socketPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bind control socket at {Path}; GUI Stop button will be unavailable.", _socketPath);
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Socket client;
                try
                {
                    client = await _listener.AcceptAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Control socket accept threw; continuing.");
                    continue;
                }
                // Fire-and-forget: a slow client must not block the accept loop.
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        finally
        {
            try { _listener?.Close(); } catch { /* best-effort */ }
            try { if (_socketPath is not null && File.Exists(_socketPath)) File.Delete(_socketPath); }
            catch { /* best-effort */ }
        }
    }

    private async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        try
        {
            using var stream = new NetworkStream(client, ownsSocket: true);
            // Cap the request payload so a misbehaving client can't grow the
            // daemon's memory by streaming garbage. Real requests are well
            // under 1 KiB.
            var buffer = new byte[4096];
            using var ms = new MemoryStream();
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(5));

            while (true)
            {
                int n;
                try
                {
                    n = await stream.ReadAsync(buffer, readCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await WriteResponseAsync(stream, new ControlResponse(false, Error: "timeout"), ct).ConfigureAwait(false);
                    return;
                }
                if (n <= 0) break;
                ms.Write(buffer, 0, n);
                if (ms.Length > 4096)
                {
                    await WriteResponseAsync(stream, new ControlResponse(false, Error: "request too large"), ct).ConfigureAwait(false);
                    return;
                }
                // Try parsing what we have so far; clients typically write
                // one JSON object and close, so we'll see EOF after one pass.
                // Trailing whitespace is fine because Deserialize tolerates it.
            }

            ControlRequest? req;
            try
            {
                req = JsonSerializer.Deserialize<ControlRequest>(
                    ms.ToArray(), ControlProtocolJson.Options);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not parse control request.");
                await WriteResponseAsync(stream, new ControlResponse(false, Error: "malformed request"), ct).ConfigureAwait(false);
                return;
            }

            var resp = await DispatchAsync(req, ct).ConfigureAwait(false);
            await WriteResponseAsync(stream, resp, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Control client handler threw.");
            try { client.Close(); } catch { /* best-effort */ }
        }
    }

    private async Task<ControlResponse> DispatchAsync(ControlRequest? req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrEmpty(req.Command))
        {
            return new ControlResponse(false, Error: "missing command");
        }
        switch (req.Command)
        {
            case ControlCommands.StopRecording:
                var stopped = await _controller.StopCurrentAsync(ct).ConfigureAwait(false);
                return stopped
                    ? new ControlResponse(true, State: "idle")
                    : new ControlResponse(false, State: "idle", Error: "no active recording");
            case ControlCommands.StartManualRecording:
                var started = await _controller.StartManualAsync(
                    DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
                return started
                    ? new ControlResponse(true, State: "recording")
                    : new ControlResponse(false, State: "recording",
                        Error: "a recording is already in progress");
            default:
                return new ControlResponse(false, Error: $"unknown command: {req.Command}");
        }
    }

    private static async Task WriteResponseAsync(Stream stream, ControlResponse resp, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(resp, ControlProtocolJson.Options);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
