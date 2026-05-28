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

using Microsoft.Extensions.Logging;

namespace Wipelog.Core.Recording;

/// <summary>
/// Runs a persistent <c>gpu-screen-recorder</c> in replay-buffer mode so the
/// daemon can prepend the last N seconds of gameplay to each encounter clip.
/// gsr keeps a ring buffer in RAM (or disk, per the recorder's
/// <see cref="GpuScreenRecorderOptions.ReplayStorage"/>) and writes a single
/// file to its output directory each time it receives SIGUSR1 via
/// <see cref="IRecorder.SaveReplayAsync"/>.
/// </summary>
public interface IPreRollRecorder : IAsyncDisposable
{
    /// <summary>
    /// Mirror of the wrapped <see cref="IRecorder.State"/> so the controller
    /// can decide whether to attempt a save.
    /// </summary>
    RecorderState State { get; }

    /// <summary>Re-raised from the wrapped recorder for the controller to handle.</summary>
    event EventHandler<RecorderExitedUnexpectedlyEventArgs>? ExitedUnexpectedly;

    /// <summary>
    /// Launches the persistent replay-buffer subprocess. Idempotent: a second
    /// call while running is a no-op. Re-callable after a clean
    /// <see cref="StopAsync"/> or unexpected exit.
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Sends SIGUSR1 to flush the buffer, then waits for the resulting .mp4
    /// to appear in the output directory and its size to settle. Returns the
    /// path of the saved file. Returns <c>null</c> if the recorder isn't
    /// running, the signal fails, or the file never materialises before the
    /// timeout.
    /// </summary>
    Task<string?> SaveAsync(CancellationToken ct);

    /// <summary>Stops the persistent subprocess.</summary>
    Task StopAsync(CancellationToken ct);
}

/// <summary>
/// Default <see cref="IPreRollRecorder"/> backed by an <see cref="IRecorder"/>
/// instance configured for replay-buffer mode (i.e. with
/// <see cref="GpuScreenRecorderOptions.ReplayBufferSeconds"/> set). The
/// wrapped recorder writes saved replays into <see cref="OutputDirectory"/>
/// with auto-generated filenames; we snapshot the directory before SIGUSR1
/// and poll for the new entry afterwards.
/// </summary>
public sealed class PreRollRecorder : IPreRollRecorder
{
    private static readonly TimeSpan DefaultSaveTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);
    private const int StableTicksThreshold = 2;

    private readonly IRecorder _recorder;
    private readonly ILogger<PreRollRecorder> _logger;
    private readonly TimeSpan _saveTimeout;
    private readonly TimeSpan _pollInterval;

    public string OutputDirectory { get; }

    public PreRollRecorder(
        IRecorder recorder,
        string outputDirectory,
        ILogger<PreRollRecorder> logger,
        TimeSpan? saveTimeout = null,
        TimeSpan? pollInterval = null)
    {
        _recorder = recorder;
        OutputDirectory = outputDirectory;
        _logger = logger;
        _saveTimeout = saveTimeout ?? DefaultSaveTimeout;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _recorder.ExitedUnexpectedly += OnRecorderExitedUnexpectedly;
    }

    public RecorderState State => _recorder.State;

    public event EventHandler<RecorderExitedUnexpectedlyEventArgs>? ExitedUnexpectedly;

    private void OnRecorderExitedUnexpectedly(object? sender, RecorderExitedUnexpectedlyEventArgs args)
    {
        try
        {
            ExitedUnexpectedly?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pre-roll ExitedUnexpectedly subscriber threw.");
        }
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_recorder.State == RecorderState.Recording)
        {
            return;
        }
        Directory.CreateDirectory(OutputDirectory);
        _logger.LogInformation(
            "Starting pre-roll recorder → buffer flushes will land in {Dir}", OutputDirectory);
        await _recorder.StartAsync(OutputDirectory, ct).ConfigureAwait(false);
    }

    public async Task<string?> SaveAsync(CancellationToken ct)
    {
        if (_recorder.State != RecorderState.Recording)
        {
            _logger.LogWarning(
                "SaveAsync called but pre-roll recorder is in state {State}; skipping pre-roll for this clip.",
                _recorder.State);
            return null;
        }

        HashSet<string> before;
        try
        {
            before = SnapshotMp4s();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not snapshot pre-roll directory {Dir}; skipping pre-roll for this clip.",
                OutputDirectory);
            return null;
        }

        try
        {
            await _recorder.SaveReplayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SIGUSR1 to pre-roll recorder failed; skipping pre-roll for this clip.");
            return null;
        }

        return await WaitForNewFileAsync(before, ct).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _recorder.StopAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _recorder.ExitedUnexpectedly -= OnRecorderExitedUnexpectedly;
        await _recorder.DisposeAsync().ConfigureAwait(false);
    }

    private HashSet<string> SnapshotMp4s()
    {
        if (!Directory.Exists(OutputDirectory))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
        return new HashSet<string>(
            Directory.EnumerateFiles(OutputDirectory, "*.mp4", SearchOption.TopDirectoryOnly),
            StringComparer.Ordinal);
    }

    private async Task<string?> WaitForNewFileAsync(HashSet<string> before, CancellationToken ct)
    {
        // gsr writes the flushed buffer asynchronously: the file appears,
        // grows, then closes. Poll the dir for an entry not in `before`,
        // then wait for size to stay constant across two consecutive polls
        // so we don't hand back a half-written mp4.
        var deadline = DateTime.UtcNow + _saveTimeout;
        string? newFile = null;
        long lastSize = -1;
        int stableTicks = 0;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            if (newFile is null)
            {
                try
                {
                    foreach (var path in Directory.EnumerateFiles(
                                 OutputDirectory, "*.mp4", SearchOption.TopDirectoryOnly))
                    {
                        if (!before.Contains(path))
                        {
                            newFile = path;
                            break;
                        }
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    // Race with directory disappearance — keep polling, the
                    // outer timeout will end it if this persists.
                }
                continue;
            }

            long size;
            try
            {
                size = new FileInfo(newFile).Length;
            }
            catch
            {
                continue;
            }

            if (size > 0 && size == lastSize)
            {
                stableTicks++;
                if (stableTicks >= StableTicksThreshold)
                {
                    return newFile;
                }
            }
            else
            {
                stableTicks = 0;
                lastSize = size;
            }
        }

        if (newFile is null)
        {
            _logger.LogWarning(
                "Pre-roll save timed out after {Timeout}s: no new file appeared in {Dir}.",
                _saveTimeout.TotalSeconds, OutputDirectory);
            return null;
        }
        _logger.LogWarning(
            "Pre-roll save timed out after {Timeout}s: {File} did not stabilize; returning anyway.",
            _saveTimeout.TotalSeconds, newFile);
        return newFile;
    }
}
