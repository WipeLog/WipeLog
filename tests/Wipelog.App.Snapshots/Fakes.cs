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

using LibVLCSharp.Shared;
using Wipelog.App.Services;
using Wipelog.Core.Preferences;
using Wipelog.Core.Recording;
using Wipelog.Core.Status;
using Wipelog.Core.Time;
using Wipelog.Library;

namespace Wipelog.App.Snapshots;

/// <summary>
/// No libvlc. <see cref="NativePlayer"/> is null, so PlayerView's VideoView
/// renders nothing (blank) — the transport bar, scrub bar and timeline still
/// render from the state this engine reports.
/// </summary>
public sealed class FakePlaybackEngine : IPlaybackEngine
{
    /// <summary>Length reported on <see cref="Load"/>.</summary>
    public long LengthMs { get; set; } = 312_000;

    /// <summary>Position reported on <see cref="Load"/> (clip is left paused here).</summary>
    public long StartPositionMs { get; set; } = 95_000;

    public MediaPlayer? NativePlayer => null;
    public bool HasLoadedMedia { get; private set; }
    public bool IsPlaying { get; private set; }
    public long Time { get; set; }
    public int Volume { get; set; } = 80;
    public int AudioTrack => 1;

    public IReadOnlyList<AudioTrackInfo> AudioTracks { get; } =
        [new(-1, "Disable"), new(1, "Game"), new(2, "Microphone")];

    public event EventHandler<long>? TimeChanged;
    public event EventHandler<long>? LengthChanged;
    public event EventHandler? StateChanged;
    public event EventHandler? AudioTracksChanged;

    public void Load(string videoPath)
    {
        HasLoadedMedia = true;
        IsPlaying = false;
        Time = StartPositionMs;
        LengthChanged?.Invoke(this, LengthMs);
        TimeChanged?.Invoke(this, Time);
        StateChanged?.Invoke(this, EventArgs.Empty);
        AudioTracksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Play() { IsPlaying = true; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void Pause() { IsPlaying = false; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void SetPause(bool pause) { IsPlaying = !pause; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void Stop() { IsPlaying = false; StateChanged?.Invoke(this, EventArgs.Empty); }
    public void NextFrame() { }
    public void SetRate(float rate) { }
    public void SetAudioTrack(int id) { }
    public void Dispose() { }
}

/// <summary>Daemon heartbeat the test sets directly; null = offline.</summary>
public sealed class FakeDaemonStatusReader : IDaemonStatusReader
{
    private DaemonStatus? _current;

    public DaemonStatus? Current
    {
        get => _current;
        set
        {
            _current = value;
            StatusChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<DaemonStatus?>? StatusChanged;
}

public sealed class FakeDaemonLifecycleController : IDaemonLifecycleController
{
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task RestartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task OpenJournalAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class FakeDaemonControlClient : IDaemonControlClient
{
    public Task<ControlCallResult> StopRecordingAsync(CancellationToken ct) =>
        Task.FromResult(new ControlCallResult(ControlCallStatus.Success));
    public Task<ControlCallResult> StartManualRecordingAsync(CancellationToken ct) =>
        Task.FromResult(new ControlCallResult(ControlCallStatus.Success));
}

public sealed class FakeServiceInstaller(string unitPath) : IServiceInstaller
{
    public ServiceInstallState State { get; set; } = ServiceInstallState.Running;
    public string ServiceFilePath => unitPath;
    public Task<ServiceInstallState> GetStateAsync(CancellationToken ct) => Task.FromResult(State);
    public Task<ServiceOperationResult> InstallAsync(DaemonConfig config, CancellationToken ct) =>
        Task.FromResult(new ServiceOperationResult(true, "Installed."));
    public Task<ServiceOperationResult> UninstallAsync(CancellationToken ct) =>
        Task.FromResult(new ServiceOperationResult(true, "Uninstalled."));
    public Task<ServiceOperationResult> ReinstallAsync(DaemonConfig config, CancellationToken ct) =>
        Task.FromResult(new ServiceOperationResult(true, "Reinstalled."));
}

public sealed class FakeWowLogDetector : IWowLogDetector
{
    public List<DetectedLogPath> Results { get; } = [];
    public IReadOnlyList<DetectedLogPath> Detect() => Results;
}

/// <summary>Returns <see cref="Document"/> for every clip (null = no sidecar).</summary>
public sealed class FakeClipEventsLoader : IClipEventsLoader
{
    public TimelineEventDocument? Document { get; set; }
    public Task<TimelineEventDocument?> LoadAsync(string videoPath, CancellationToken ct) =>
        Task.FromResult(Document);
}

/// <summary>Never shows UI: confirm = yes, pickers = cancelled.</summary>
public sealed class FakeDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message,
        string confirmLabel = "Delete", string cancelLabel = "Cancel") => Task.FromResult(true);
    public Task<string?> SaveFilePickerAsync(string suggestedName, string filterName,
        IReadOnlyList<string> extensions, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<T> ShowProgressAsync<T>(string title,
        Func<IProgress<ProgressUpdate>, CancellationToken, Task<T>> work) =>
        work(new Progress<ProgressUpdate>(_ => { }), CancellationToken.None);
}

public sealed class FakeClipArchiver : IClipArchiver
{
    public Task<ArchiveResult> ArchiveAsync(IReadOnlyList<ClipRecord> clips,
        string outputPath, IProgress<ArchiveProgress>? progress, CancellationToken ct) =>
        Task.FromResult(new ArchiveResult(clips.Count, 0, 0, outputPath, false));
}

public sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
    public TimeZoneInfo Zone => TimeZoneInfo.Utc;
}
