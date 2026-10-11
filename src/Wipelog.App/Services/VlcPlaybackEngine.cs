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

namespace Wipelog.App.Services;

/// <summary>
/// <see cref="IPlaybackEngine"/> over a LibVLC <see cref="MediaPlayer"/>.
/// Forwards VLC's events as-is (on VLC's threads).
/// </summary>
public sealed class VlcPlaybackEngine : IPlaybackEngine
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private Media? _currentMedia;
    private bool _disposed;

    public VlcPlaybackEngine(LibVLC libVlc)
    {
        _libVlc = libVlc;
        _player = new MediaPlayer(libVlc);
        // The X11 vout module otherwise grabs keyboard input on the embedded
        // subwindow whenever the cursor is over it, and those events never
        // reach Avalonia's Tunnel KeyDown handler — so player shortcuts go
        // dead while the mouse hovers the video.
        _player.EnableKeyInput = false;
        _player.EnableMouseInput = false;
        _player.TimeChanged += OnTimeChanged;
        _player.LengthChanged += OnLengthChanged;
        _player.Playing += OnStateChanged;
        _player.Paused += OnStateChanged;
        _player.Stopped += OnStateChanged;
        _player.EndReached += OnStateChanged;
        _player.Playing += OnTracksChanged;
        _player.ESAdded += OnEsAdded;
    }

    public MediaPlayer? NativePlayer => _player;
    public bool HasLoadedMedia => _currentMedia is not null;
    public bool IsPlaying => _player.IsPlaying;

    public long Time
    {
        get => _player.Time;
        set => _player.Time = value;
    }

    public int Volume
    {
        get => _player.Volume;
        set => _player.Volume = value;
    }

    public int AudioTrack => _player.AudioTrack;

    public IReadOnlyList<AudioTrackInfo> AudioTracks =>
        _player.AudioTrackDescription is { } descriptions
            ? descriptions.Select(d => new AudioTrackInfo(d.Id, d.Name)).ToList()
            : [];

    public event EventHandler<long>? TimeChanged;
    public event EventHandler<long>? LengthChanged;
    public event EventHandler? StateChanged;
    public event EventHandler? AudioTracksChanged;

    public void Load(string videoPath)
    {
        _currentMedia?.Dispose();
        _currentMedia = new Media(_libVlc, videoPath, FromType.FromPath);
        _player.Play(_currentMedia);
    }

    public void Play() => _player.Play();
    public void Pause() => _player.Pause();
    public void SetPause(bool pause) => _player.SetPause(pause);
    public void Stop() => _player.Stop();
    public void NextFrame() => _player.NextFrame();
    public void SetRate(float rate) => _player.SetRate(rate);
    public void SetAudioTrack(int id) => _player.SetAudioTrack(id);

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e) => TimeChanged?.Invoke(this, e.Time);
    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e) => LengthChanged?.Invoke(this, e.Length);
    private void OnStateChanged(object? sender, EventArgs e) => StateChanged?.Invoke(this, EventArgs.Empty);
    private void OnTracksChanged(object? sender, EventArgs e) => AudioTracksChanged?.Invoke(this, EventArgs.Empty);
    private void OnEsAdded(object? sender, MediaPlayerESAddedEventArgs e) => AudioTracksChanged?.Invoke(this, EventArgs.Empty);

    // Idempotent: both PlayerViewModel.Dispose and the DI container own it.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _player.Stop(); } catch { }
        _player.TimeChanged -= OnTimeChanged;
        _player.LengthChanged -= OnLengthChanged;
        _player.Playing -= OnStateChanged;
        _player.Paused -= OnStateChanged;
        _player.Stopped -= OnStateChanged;
        _player.EndReached -= OnStateChanged;
        _player.Playing -= OnTracksChanged;
        _player.ESAdded -= OnEsAdded;
        _currentMedia?.Dispose();
        _player.Dispose();
    }
}
