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

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Detection;
using Wipelog.Core.Preferences;

namespace Wipelog.Daemon.Configuration;

/// <summary>
/// Watches <c>preferences.json</c> and hot-swaps the recording filters into the
/// live <see cref="EncounterDetector"/> whenever the GUI saves it, so changes to
/// the CONTENT settings (raid difficulties, minimum keystone level, BG toggle)
/// take effect without a service restart. Only the filters are reloaded — the
/// daemon's other settings are baked into its command-line args and still need a
/// reinstall.
/// </summary>
internal sealed class RecordingFilterReloader : BackgroundService
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly EncounterDetector _detector;
    private readonly ILogger<RecordingFilterReloader> _logger;
    private readonly UserPreferencesService _preferences;
    private readonly string _path;

    private readonly object _gate = new();
    private CancellationTokenSource? _debounce;

    public RecordingFilterReloader(
        EncounterDetector detector,
        ILogger<RecordingFilterReloader> logger,
        UserPreferencesService? preferences = null)
    {
        _detector = detector;
        _logger = logger;
        _preferences = preferences ?? new UserPreferencesService();
        _path = _preferences.Path;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var dir = Path.GetDirectoryName(_path);
        var file = Path.GetFileName(_path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file) || !Directory.Exists(dir))
        {
            // Nothing to watch yet (fresh machine, no preferences saved). Filter
            // changes will apply on the next daemon restart in this rare case.
            _logger.LogDebug(
                "Preferences directory {Dir} not present; recording-filter hot-reload disabled.", dir);
            return Task.CompletedTask;
        }

        // The GUI saves atomically (write .tmp + rename), so the interesting
        // events are Created/Renamed as well as Changed. The filter on the exact
        // file name keeps the .tmp churn out.
        var watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.EnableRaisingEvents = true;

        _logger.LogInformation("Watching {Path} for recording-filter changes.", _path);

        // Keep the service alive until shutdown, then tear the watcher down.
        return WaitUntilStoppedAsync(watcher, stoppingToken);
    }

    private async Task WaitUntilStoppedAsync(FileSystemWatcher watcher, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            lock (_gate)
            {
                _debounce?.Cancel();
                _debounce?.Dispose();
                _debounce = null;
            }
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Coalesce the burst of events a single save produces into one reload.
        lock (_gate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = new CancellationTokenSource();
            var token = _debounce.Token;
            _ = ReloadAfterDelayAsync(token);
        }
    }

    private async Task ReloadAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceDelay, token).ConfigureAwait(false);
            ReloadNow();
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Re-read preferences and push the filters into the detector. Internal so
    /// tests can drive it deterministically without the watcher/debounce timing.
    /// </summary>
    internal void ReloadNow()
    {
        try
        {
            var filters = _preferences.Load().RecordingFilters;
            _detector.UpdateFilters(filters);
            _logger.LogInformation(
                "Recording filters reloaded from {Path} (raids={Raids}, mythicPlus={MythicPlus} min +{MinKey}, battlegrounds={Battlegrounds}).",
                _path,
                filters.RecordRaids,
                filters.RecordMythicPlus,
                filters.MinKeystoneLevel,
                filters.RecordBattlegrounds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload recording filters from {Path}.", _path);
        }
    }
}
