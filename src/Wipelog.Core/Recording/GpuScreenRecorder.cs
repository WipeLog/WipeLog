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

using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Preferences;

namespace Wipelog.Core.Recording;

/// <summary>
/// Wraps the <c>gpu-screen-recorder</c> CLI as a controllable subprocess.
/// Linux-only: uses <c>kill(2)</c> via libc to send SIGINT for graceful stop
/// and SIGUSR1 to flush the replay buffer.
/// </summary>
public sealed class GpuScreenRecorder : IRecorder
{
    private static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(10);
    private const int StderrTailCapacity = 50;

    private readonly GpuScreenRecorderOptions _options;
    private readonly ILogger<GpuScreenRecorder> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stderrLock = new();
    private readonly Queue<string> _stderrTail = new(StderrTailCapacity);

    private Process? _process;
    // Set right before we deliberately signal the process so the Exited
    // handler can distinguish "we asked for it" from "it died on its own".
    // 0 = unexpected, 1 = stopping deliberately.
    private int _deliberateStop;

    // Resolved once on first start; persists for the lifetime of this
    // recorder so a multi-clip session doesn't re-shell-out for every Start.
    private string? _autoCaptureSize;

    public event EventHandler<RecorderExitedUnexpectedlyEventArgs>? ExitedUnexpectedly;

    public GpuScreenRecorder(GpuScreenRecorderOptions options, ILogger<GpuScreenRecorder> logger)
    {
        _options = options;
        _logger = logger;
    }

    public RecorderState State { get; private set; } = RecorderState.Idle;

    public async Task StartAsync(string outputPath, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (State != RecorderState.Idle)
            {
                throw new InvalidOperationException(
                    $"Cannot start: recorder is in state {State}. Stop the current recording first.");
            }

            EnsureExecutableExists(_options.ExecutablePath);
            State = RecorderState.Starting;
            ClearStderrTail();
            Interlocked.Exchange(ref _deliberateStop, 0);

            var captureSize = await ResolveCaptureSizeAsync(ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in BuildArgs(outputPath, _options, captureSize))
            {
                psi.ArgumentList.Add(arg);
            }

            _logger.LogInformation(
                "Starting gpu-screen-recorder: {Executable} {Args}",
                psi.FileName,
                string.Join(' ', psi.ArgumentList));

            Process? proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                State = RecorderState.Failed;
                throw new InvalidOperationException(
                    $"Failed to start '{_options.ExecutablePath}': {ex.Message}", ex);
            }

            if (proc is null)
            {
                State = RecorderState.Failed;
                throw new InvalidOperationException(
                    $"Process.Start returned null for '{_options.ExecutablePath}'.");
            }

            _process = proc;
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    _logger.LogDebug("[gsr stdout] {Line}", e.Data);
                }
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                _logger.LogDebug("[gsr stderr] {Line}", e.Data);
                AppendStderrLine(e.Data);
            };
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            State = RecorderState.Recording;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_process is null)
            {
                return;
            }

            // If the process already died on us (Failed via OnProcessExited),
            // we still own the Process handle — clean it up and return idle.
            if (State == RecorderState.Failed)
            {
                _process.Dispose();
                _process = null;
                State = RecorderState.Idle;
                return;
            }

            if (State != RecorderState.Recording)
            {
                return;
            }
            // Mark deliberate stop BEFORE signalling so the Exited handler
            // suppresses the unexpected-exit notification.
            Interlocked.Exchange(ref _deliberateStop, 1);
            State = RecorderState.Stopping;

            int pid = _process.Id;
            int rc = LibC.Kill(pid, LibC.SIGINT);
            if (rc != 0)
            {
                _logger.LogWarning(
                    "kill(SIGINT) returned {Rc} for pid {Pid} — process likely already exited.", rc, pid);
            }

            using var grace = CancellationTokenSource.CreateLinkedTokenSource(ct);
            grace.CancelAfter(StopGracePeriod);
            try
            {
                await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "gpu-screen-recorder did not exit within {Grace}s of SIGINT, sending SIGKILL.",
                    StopGracePeriod.TotalSeconds);
                LibC.Kill(pid, LibC.SIGKILL);
                await _process.WaitForExitAsync(ct).ConfigureAwait(false);
            }

            _process.Dispose();
            _process = null;
            State = RecorderState.Idle;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SaveReplayAsync(CancellationToken ct)
    {
        if (State != RecorderState.Recording || _process is null)
        {
            throw new InvalidOperationException(
                "SaveReplayAsync can only be called while recording in replay-buffer mode.");
        }
        if (_options.ReplayBufferSeconds is null)
        {
            throw new InvalidOperationException(
                "SaveReplayAsync requires GpuScreenRecorderOptions.ReplayBufferSeconds to be set.");
        }
        int rc = LibC.Kill(_process.Id, LibC.SIGUSR1);
        if (rc != 0)
        {
            _logger.LogWarning("kill(SIGUSR1) returned {Rc} for pid {Pid}", rc, _process.Id);
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (State == RecorderState.Recording)
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping recorder during disposal.");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _gate.Dispose();
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        // If StopAsync set the flag, this is a clean shutdown — let StopAsync
        // finish its WaitForExitAsync without firing any error event.
        if (Interlocked.CompareExchange(ref _deliberateStop, 0, 0) != 0)
        {
            return;
        }
        // If we were never in Recording (e.g., startup error path), there is
        // nothing meaningful to report here either.
        if (State != RecorderState.Recording)
        {
            return;
        }

        int exitCode = -1;
        try { exitCode = _process?.ExitCode ?? -1; }
        catch { /* process handle may be torn down */ }

        var tail = GetStderrTail();
        State = RecorderState.Failed;
        _logger.LogWarning(
            "gpu-screen-recorder exited unexpectedly (exitCode={ExitCode}). Last stderr lines:\n{Tail}",
            exitCode, string.IsNullOrEmpty(tail) ? "(none captured)" : tail);

        try
        {
            ExitedUnexpectedly?.Invoke(this, new RecorderExitedUnexpectedlyEventArgs(exitCode, tail));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ExitedUnexpectedly subscriber threw.");
        }
    }

    private void AppendStderrLine(string line)
    {
        lock (_stderrLock)
        {
            if (_stderrTail.Count >= StderrTailCapacity)
            {
                _stderrTail.Dequeue();
            }
            _stderrTail.Enqueue(line);
        }
    }

    private void ClearStderrTail()
    {
        lock (_stderrLock)
        {
            _stderrTail.Clear();
        }
    }

    private string GetStderrTail()
    {
        lock (_stderrLock)
        {
            return _stderrTail.Count == 0 ? string.Empty : string.Join('\n', _stderrTail);
        }
    }

    internal static IEnumerable<string> BuildArgs(
        string outputPath,
        GpuScreenRecorderOptions o,
        string? resolvedCaptureSize = null)
    {
        yield return "-w"; yield return o.Window;
        var size = string.IsNullOrWhiteSpace(o.CaptureSize) ? resolvedCaptureSize : o.CaptureSize;
        if (!string.IsNullOrWhiteSpace(size))
        {
            yield return "-s"; yield return size;
        }
        yield return "-f"; yield return o.Framerate.ToString(CultureInfo.InvariantCulture);
        yield return "-k"; yield return o.Codec;
        var tracks = o.AudioTracks is { Count: > 0 }
            ? o.AudioTracks
            : new[] { "default_output" };
        foreach (var track in tracks)
        {
            yield return "-a"; yield return track;
        }
        yield return "-c"; yield return o.Container;
        if (o.ReplayBufferSeconds is { } replay)
        {
            yield return "-r";
            yield return replay.ToString(CultureInfo.InvariantCulture);
        }
        yield return "-q"; yield return o.Quality;
        yield return "-o"; yield return outputPath;
    }

    /// <summary>
    /// gpu-screen-recorder requires <c>-s WxH</c> whenever <c>-w focused</c>
    /// is used. When the caller didn't supply <see cref="GpuScreenRecorderOptions.CaptureSize"/>
    /// we ask gsr itself for its monitor list and pick the right one (the
    /// configured monitor by name, or the largest in focused mode) so the
    /// focus capture box covers any attached display. When
    /// <see cref="GpuScreenRecorderOptions.TargetHeight"/> is set, the source
    /// dimensions are scaled to that height (aspect preserved) so the encoder
    /// writes a smaller video. Result is cached per-recorder so we don't
    /// re-shell-out for every clip.
    /// </summary>
    private async Task<string?> ResolveCaptureSizeAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.CaptureSize)) return _options.CaptureSize;
        var focused = string.Equals(_options.Window, "focused", StringComparison.Ordinal);
        // No downscale + monitor-specific capture: gsr already knows the
        // source size from -w, so omit -s (matches pre-resolution behaviour).
        if (!focused && _options.TargetHeight is null) return null;
        if (_autoCaptureSize is not null) return _autoCaptureSize;

        try
        {
            var monitors = await GpuScreenRecorderMonitorList
                .QueryAsync(_options.ExecutablePath, ct).ConfigureAwait(false);
            var pick = focused
                ? GpuScreenRecorderMonitorList.PickLargest(monitors)
                : (monitors.FirstOrDefault(m =>
                       string.Equals(m.Name, _options.Window, StringComparison.Ordinal))
                   ?? GpuScreenRecorderMonitorList.PickLargest(monitors));
            if (pick is null)
            {
                _logger.LogWarning(
                    "gpu-screen-recorder --list-monitors returned no parseable monitors; " +
                    "starting without -s.");
                return null;
            }

            var scaled = VideoResolutions.ResolveCaptureSize(pick.Width, pick.Height, _options.TargetHeight);
            if (scaled is not null)
            {
                _autoCaptureSize = scaled;
                _logger.LogInformation(
                    "Scaling output to {Size} from monitor {Name} {Native} (target {Target}p).",
                    _autoCaptureSize, pick.Name, $"{pick.Width}x{pick.Height}", _options.TargetHeight);
            }
            else
            {
                _autoCaptureSize = $"{pick.Width}x{pick.Height}";
                _logger.LogInformation(
                    "Auto-resolved capture size {Size} from monitor {Name}.",
                    _autoCaptureSize, pick.Name);
            }
            return _autoCaptureSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to auto-detect capture size via --list-monitors; proceeding without -s.");
            return null;
        }
    }

    private static void EnsureExecutableExists(string path)
    {
        if (Path.IsPathRooted(path))
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"gpu-screen-recorder not found at '{path}'. " +
                    "Install it via your package manager or flatpak (flathub.com/apps/com.dec05eba.gpu_screen_recorder).",
                    path);
            }
            return;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate)) return;
        }
        throw new FileNotFoundException(
            $"'{path}' not found in PATH. " +
            "Install gpu-screen-recorder via your package manager or flatpak " +
            "(flathub.com/apps/com.dec05eba.gpu_screen_recorder), " +
            "or set GpuScreenRecorderOptions.ExecutablePath to an absolute path.");
    }
}
