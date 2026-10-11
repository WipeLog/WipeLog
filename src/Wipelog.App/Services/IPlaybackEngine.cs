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
/// The slice of a media player that <see cref="ViewModels.PlayerViewModel"/>
/// drives. Production is <see cref="VlcPlaybackEngine"/>; tests substitute a
/// fake so the player (and every view that hosts it) can be built without
/// libvlc. Events may be raised on any thread — the view model marshals.
/// </summary>
public interface IPlaybackEngine : IDisposable
{
    /// <summary>
    /// The LibVLC player the <c>VideoView</c> renders into, or null for
    /// engines with no native surface (the view then shows nothing).
    /// </summary>
    MediaPlayer? NativePlayer { get; }

    /// <summary>True once <see cref="Load"/> has been handed a clip.</summary>
    bool HasLoadedMedia { get; }

    bool IsPlaying { get; }

    /// <summary>Playback position in ms. Setting it seeks.</summary>
    long Time { get; set; }

    /// <summary>0..100.</summary>
    int Volume { get; set; }

    /// <summary>Active audio track id (-1 = disabled).</summary>
    int AudioTrack { get; }

    /// <summary>Audio streams in the current clip, as the engine reports them.</summary>
    IReadOnlyList<AudioTrackInfo> AudioTracks { get; }

    /// <summary>Replaces the current clip and starts playing it.</summary>
    void Load(string videoPath);

    void Play();
    void Pause();
    void SetPause(bool pause);
    void Stop();
    void NextFrame();
    void SetRate(float rate);
    void SetAudioTrack(int id);

    /// <summary>Position changed; argument is the new time in ms.</summary>
    event EventHandler<long>? TimeChanged;

    /// <summary>Clip length became known; argument is the length in ms.</summary>
    event EventHandler<long>? LengthChanged;

    /// <summary>Playing / paused / stopped / end-reached.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when <see cref="AudioTracks"/> may have changed.</summary>
    event EventHandler? AudioTracksChanged;
}

/// <summary>One audio stream as reported by the engine.</summary>
public readonly record struct AudioTrackInfo(int Id, string? Name);
