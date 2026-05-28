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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Status;

namespace Wipelog.Daemon.Status;

internal sealed class HeartbeatWriter : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly HeartbeatState _state;
    private readonly string _statusPath;
    private readonly ILogger<HeartbeatWriter> _logger;

    public HeartbeatWriter(
        HeartbeatState state,
        ILogger<HeartbeatWriter> logger,
        string? statusPath = null)
    {
        _state = state;
        _logger = logger;
        _statusPath = statusPath ?? DefaultStatusPath();
    }

    public string StatusPath => _statusPath;

    public static string DefaultStatusPath()
    {
        var cache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (string.IsNullOrWhiteSpace(cache))
        {
            cache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache");
        }
        return Path.Combine(cache, "wipelog", "status.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statusPath)!);
        WriteOnce();

        try
        {
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                WriteOnce();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WriteOnce(_state.FinalShutdownSnapshot());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write final heartbeat on shutdown.");
        }
        try
        {
            if (File.Exists(_statusPath)) File.Delete(_statusPath);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete heartbeat file on shutdown.");
        }
    }

    private void WriteOnce() => WriteOnce(_state.Snapshot());

    private void WriteOnce(DaemonStatus status)
    {
        var tmp = _statusPath + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                JsonSerializer.Serialize(fs, status, JsonOptions);
            }
            // Linux rename is atomic on the same filesystem — readers never see
            // a half-written file. File.Move with overwrite delegates to rename.
            File.Move(tmp, _statusPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to write heartbeat at {Path}", _statusPath);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
        }
    }
}
