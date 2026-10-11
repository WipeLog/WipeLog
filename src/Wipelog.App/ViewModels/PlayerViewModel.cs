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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using Wipelog.App.Services;

namespace Wipelog.App.ViewModels;

public sealed partial class PlayerViewModel : ViewModelBase, IDisposable
{
    private readonly IPlaybackEngine _engine;
    private readonly ClipListViewModel _list;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly TimelinePreferencesService _prefsService;
    private string? _currentVideoPath;

    // The VideoView's render target; null when the engine has no native
    // surface (tests), in which case the video area stays blank.
    public MediaPlayer? MediaPlayer => _engine.NativePlayer;
    public TimelineViewModel Timeline { get; }
    public DisplayOptionsViewModel DisplayOptions { get; }
    public TimelinePreferences TimelinePreferences { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayPauseTooltip))]
    private bool _isPlaying;

    [ObservableProperty]
    private double _durationMs;

    private double _positionMs;
    public double PositionMs
    {
        get => _positionMs;
        set
        {
            if (Math.Abs(_positionMs - value) < 0.5) return;
            _positionMs = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PositionText));
            // User scrub: bound Slider sets this; commit to VLC.
            if (_isUserScrubbing && _engine.HasLoadedMedia)
            {
                _engine.Time = (long)value;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMuted))]
    private int _volume = 80;

    private int _preMuteVolume = 80;

    // Playback speed. libVLC resets rate to 1.0 on each new input, so the
    // chosen value is re-applied whenever a clip starts playing (see
    // OnVlcStateChanged). Discrete choices feed the transport-bar selector.
    [ObservableProperty]
    private double _playbackRate = 1.0;

    public IReadOnlyList<double> PlaybackRateChoices { get; } =
        new[] { 0.5, 1.0, 1.5, 2.0 };

    public string PlayPauseTooltip => IsPlaying ? "Pause (Space)" : "Play (Space)";

    // Drives the mute-icon swap in the transport bar (volume_up / volume_off
    // Material glyphs). The M shortcut and the click handler both work by
    // setting Volume to 0 / restoring the pre-mute value, so deriving this
    // from Volume keeps state in one place.
    public bool IsMuted => Volume == 0;

    [ObservableProperty]
    private bool _hasMedia;

    [ObservableProperty]
    private string? _errorMessage;

    // Available audio streams from the loaded clip. Populated on Playing /
    // ESAdded — LibVLC doesn't expose tracks until decoding starts. The
    // first entry is always the "Mute" pseudo-track (LibVLC id = -1) so the
    // user can analyse silently.
    public ObservableCollection<AudioTrackChoice> AudioTracks { get; } = new();

    [ObservableProperty]
    private AudioTrackChoice? _selectedAudioTrack;

    public bool HasMultipleAudioTracks => AudioTracks.Count > 2;

    // Loading a new clip refreshes the track list; we suppress the setter's
    // side-effect (SetAudioTrack) during that swap so we don't try to apply
    // a stale id to the new media.
    private bool _suppressAudioTrackApply;

    private bool _isUserScrubbing;

    public string PositionText => FormatTime((long)PositionMs);
    public string DurationText => FormatTime((long)DurationMs);

    public PlayerViewModel(
        IPlaybackEngine engine,
        ClipListViewModel list,
        TimelineViewModel timeline,
        DisplayOptionsViewModel displayOptions,
        TimelinePreferencesService prefsService,
        ILogger<PlayerViewModel> logger)
    {
        _engine = engine;
        _list = list;
        _logger = logger;
        _prefsService = prefsService;
        Timeline = timeline;
        DisplayOptions = displayOptions;
        TimelinePreferences = prefsService.Load();
        _engine.TimeChanged += OnVlcTimeChanged;
        _engine.LengthChanged += OnVlcLengthChanged;
        _engine.StateChanged += OnVlcStateChanged;
        _engine.AudioTracksChanged += OnVlcAudioTracksChanged;

        AudioTracks.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultipleAudioTracks));

        _list.PropertyChanged += OnListPropertyChanged;
    }

    private void OnVlcAudioTracksChanged(object? sender, EventArgs e) => RefreshAudioTracks();

    private void RefreshAudioTracks()
    {
        // VLC fires events off-thread; marshal so the ObservableCollection
        // mutations land on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            var descriptions = _engine.AudioTracks;
            var fresh = new List<AudioTrackChoice>
            {
                new AudioTrackChoice(-1, "Mute (no audio)"),
            };

            int n = 1;
            foreach (var d in descriptions)
            {
                // VLC also surfaces "Disable" as id=-1 — we already added our own.
                if (d.Id < 0) continue;
                var name = string.IsNullOrWhiteSpace(d.Name) ? $"Audio {n}" : d.Name;
                fresh.Add(new AudioTrackChoice(d.Id, $"Track {n} · {name}"));
                n++;
            }

            // Avoid no-op churn: if the list is structurally identical to what's
            // already shown, leave it (and the user's current pick) alone.
            if (fresh.Count == AudioTracks.Count)
            {
                bool same = true;
                for (int i = 0; i < fresh.Count; i++)
                {
                    if (AudioTracks[i].Id != fresh[i].Id) { same = false; break; }
                }
                if (same) return;
            }

            _suppressAudioTrackApply = true;
            try
            {
                AudioTracks.Clear();
                foreach (var t in fresh) AudioTracks.Add(t);

                var activeId = _engine.AudioTrack;
                var pick = AudioTracks.FirstOrDefault(t => t.Id == activeId)
                    ?? AudioTracks.Skip(1).FirstOrDefault()    // first real track
                    ?? AudioTracks.FirstOrDefault();           // fall back to Mute
                SelectedAudioTrack = pick;
            }
            finally
            {
                _suppressAudioTrackApply = false;
            }
        });
    }

    partial void OnSelectedAudioTrackChanged(AudioTrackChoice? value)
    {
        if (_suppressAudioTrackApply || value is null) return;
        try { _engine.SetAudioTrack(value.Id); }
        catch (Exception ex) { _logger.LogDebug(ex, "SetAudioTrack failed for id {Id}.", value.Id); }
    }

    private void OnListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ClipListViewModel.SelectedItem)) return;
        // Keyboard ↑/↓/J/K cursor moves through the list without loading; the
        // user has to press Enter (or click) to commit. The flag is true only
        // for the synchronous PropertyChanged dispatch from SelectPrevious/Next.
        if (_list.IsKeyboardNavigating) return;
        var selected = _list.SelectedItem;
        if (selected is null) return;
        // RefreshAsync replaces ClipListItemViewModel instances wholesale, then
        // reassigns SelectedItem to a new instance with the same Id — that fires
        // PropertyChanged even though the user's selection didn't change. Without
        // this guard, a recording finishing while the app is open would restart
        // playback of whatever was selected.
        if (string.Equals(selected.Record.VideoPath, _currentVideoPath, StringComparison.Ordinal)) return;
        LoadClip(selected.Record.VideoPath);
    }

    public void LoadClip(string videoPath)
    {
        try
        {
            _engine.Load(videoPath);
            _currentVideoPath = videoPath;
            HasMedia = true;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play {Path}", videoPath);
            ErrorMessage = $"Could not play clip: {ex.Message}";
            HasMedia = false;
        }
    }

    public void TogglePlayPause()
    {
        if (!HasMedia) return;
        if (_engine.IsPlaying) _engine.Pause();
        else _engine.Play();
    }

    public void SeekRelative(TimeSpan delta)
    {
        if (!HasMedia) return;
        CommitSeek((long)(PositionMs + delta.TotalMilliseconds));
    }

    public void SeekAbsolute(TimeSpan target)
    {
        if (!HasMedia) return;
        CommitSeek((long)target.TotalMilliseconds);
    }

    public void Pause()
    {
        if (!HasMedia) return;
        // SetPause(true) is a no-op when already paused — no need to guard.
        _engine.SetPause(true);
    }

    public void UpdateTimelinePreferences(TimelinePreferences prefs)
    {
        TimelinePreferences = prefs;
        _prefsService.Save(prefs);
    }

    public void Stop()
    {
        _engine.Stop();
    }

    public void StepFrame(bool forward)
    {
        if (!HasMedia) return;
        if (forward)
        {
            try { _engine.NextFrame(); }
            catch { /* not always supported by every codec; no-op */ }
        }
        else
        {
            // No native backward step in libvlc — approximate by ~16ms (60fps).
            CommitSeek(_engine.Time - 16);
        }
    }

    public void AdjustVolume(int delta)
    {
        Volume = Math.Clamp(Volume + delta, 0, 100);
    }

    public void ToggleMute()
    {
        if (Volume == 0)
        {
            Volume = _preMuteVolume == 0 ? 50 : _preMuteVolume;
        }
        else
        {
            _preMuteVolume = Volume;
            Volume = 0;
        }
    }

    public void SeekToFraction(double f)
    {
        if (!HasMedia || DurationMs <= 0) return;
        var clamped = Math.Clamp(f, 0.0, 1.0);
        CommitSeek((long)(DurationMs * clamped));
    }

    public void SeekToStart()
    {
        if (!HasMedia) return;
        CommitSeek(0);
    }

    public void SeekToEnd()
    {
        if (!HasMedia) return;
        CommitSeek((long)(DurationMs - 5000));
    }

    partial void OnVolumeChanged(int value)
    {
        _engine.Volume = Math.Clamp(value, 0, 100);
    }

    partial void OnPlaybackRateChanged(double value)
    {
        if (!HasMedia) return;
        ApplyPlaybackRate(value);
    }

    private void ApplyPlaybackRate(double rate)
    {
        try { _engine.SetRate((float)rate); }
        catch (Exception ex) { _logger.LogDebug(ex, "SetRate failed for {Rate}.", rate); }
    }

    public void BeginScrub() => _isUserScrubbing = true;
    public void EndScrub()
    {
        if (_isUserScrubbing && _engine.HasLoadedMedia)
        {
            CommitSeek((long)_positionMs);
        }
        _isUserScrubbing = false;
    }

    private void CommitSeek(long ms)
    {
        if (DurationMs <= 0) return;
        ms = (long)Math.Clamp(ms, 0, DurationMs);
        _engine.Time = ms;
        _positionMs = ms;
        OnPropertyChanged(nameof(PositionMs));
        OnPropertyChanged(nameof(PositionText));
        if (!_engine.IsPlaying)
        {
            // libvlc quirk: paused seeks don't render the new frame and don't
            // fire TimeChanged. NextFrame forces the decoder to display the
            // target frame and re-pauses.
            try { _engine.NextFrame(); }
            catch { /* not every codec supports it; harmless to skip */ }
        }
    }

    private void OnVlcTimeChanged(object? sender, long time)
    {
        // VLC fires this on its own thread — marshal to UI for the binding.
        Dispatcher.UIThread.Post(() =>
        {
            if (_isUserScrubbing) return;
            _positionMs = time;
            OnPropertyChanged(nameof(PositionMs));
            OnPropertyChanged(nameof(PositionText));
        });
    }

    private void OnVlcLengthChanged(object? sender, long length)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DurationMs = length;
            OnPropertyChanged(nameof(DurationText));
        });
    }

    private void OnVlcStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = _engine.IsPlaying;
            // libVLC resets rate to 1.0 on a fresh input; re-assert the user's
            // chosen speed once the new clip is actually playing. No-op when
            // already at 1.0 or the rate already matches.
            if (IsPlaying && Math.Abs(PlaybackRate - 1.0) > 0.001)
            {
                ApplyPlaybackRate(PlaybackRate);
            }
        });
    }

    private static string FormatTime(long ms)
    {
        if (ms < 0) ms = 0;
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    public void Dispose()
    {
        _engine.TimeChanged -= OnVlcTimeChanged;
        _engine.LengthChanged -= OnVlcLengthChanged;
        _engine.StateChanged -= OnVlcStateChanged;
        _engine.AudioTracksChanged -= OnVlcAudioTracksChanged;
        // Stops playback, then releases the media + native player.
        _engine.Dispose();
    }
}

/// <summary>
/// One row in the player's audio-track dropdown. <see cref="Id"/> is the
/// LibVLC track id passed to <c>MediaPlayer.SetAudioTrack</c>; -1 means
/// "no audio" (mute the stream entirely so it doesn't influence
/// downmix when scrubbing).
/// </summary>
public sealed record AudioTrackChoice(int Id, string Display);
