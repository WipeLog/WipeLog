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
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Status;
using Wipelog.Core.Time;

namespace Wipelog.App.Services;

/// <summary>
/// Polls the daemon's heartbeat file every <see cref="PollInterval"/> and
/// exposes the deserialized snapshot. A snapshot older than
/// <see cref="StaleThreshold"/> (or a missing file) is reported as
/// <c>null</c> = offline.
/// </summary>
public sealed class FileBasedDaemonStatusReader : IDaemonStatusReader, IDisposable
{
    public static TimeSpan PollInterval { get; } = TimeSpan.FromSeconds(2);
    public static TimeSpan StaleThreshold { get; } = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _statusPath;
    private readonly IClock _clock;
    private readonly ILogger<FileBasedDaemonStatusReader>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;

    private DaemonStatus? _current;

    public FileBasedDaemonStatusReader(
        string statusPath,
        IClock clock,
        ILogger<FileBasedDaemonStatusReader>? logger = null)
    {
        _statusPath = statusPath;
        _clock = clock;
        _logger = logger;
        _loopTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public DaemonStatus? Current => _current;

    public event EventHandler<DaemonStatus?>? StatusChanged;

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

    /// <summary>
    /// Read the file once synchronously. Exposed for tests and for the
    /// view-models that want a snapshot at construction time without waiting
    /// for the first tick.
    /// </summary>
    public DaemonStatus? ReadOnce()
    {
        var read = TryRead();
        UpdateCurrent(read);
        return read;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            do
            {
                var read = TryRead();
                UpdateCurrent(read);
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Status reader loop terminated.");
        }
    }

    private DaemonStatus? TryRead()
    {
        try
        {
            if (!File.Exists(_statusPath)) return null;
            using var fs = new FileStream(_statusPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var read = JsonSerializer.Deserialize<DaemonStatus>(fs, JsonOptions);
            if (read is null) return null;
            return _clock.Now - read.HeartbeatAt > StaleThreshold ? null : read;
        }
        catch (JsonException)
        {
            // Likely a write-in-progress crossed our read; the next poll will pick it up.
            return _current;
        }
        catch (IOException)
        {
            return _current;
        }
    }

    private void UpdateCurrent(DaemonStatus? next)
    {
        var prev = _current;
        if (StatusEquals(prev, next)) return;
        _current = next;
        StatusChanged?.Invoke(this, next);
    }

    private static bool StatusEquals(DaemonStatus? a, DaemonStatus? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        // HeartbeatAt changes every tick; ignore it so listeners only see
        // meaningful transitions.
        return a.State == b.State
            && a.CurrentRecording == b.CurrentRecording
            && a.ClipsRecordedThisSession == b.ClipsRecordedThisSession
            && a.LastError == b.LastError
            && a.Pid == b.Pid
            && a.AdvancedLoggingDisabled == b.AdvancedLoggingDisabled
            // Record equality compares the usage fields; a new measurement
            // (~once/60s) is a meaningful transition that refreshes the bar.
            && a.DiskUsage == b.DiskUsage;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loopTask.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { /* observed by RunAsync */ }
        _cts.Dispose();
    }
}
