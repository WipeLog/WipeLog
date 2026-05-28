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

namespace Wipelog.Core.Recording;

public sealed record GpuScreenRecorderOptions(
    string ExecutablePath = "gpu-screen-recorder",
    string Window = "focused",
    int Framerate = 60,
    string Codec = "h264",
    // One -a flag per entry → one audio track per entry in the mp4. When
    // null/empty the recorder falls back to a single "default_output" track,
    // matching pre-multi-track behaviour. Each entry is passed verbatim, so
    // gsr's union syntax ("app:WoW|app:Spotify") and inverse syntax
    // ("app-inverse:WoW|spotify|WEBRTC VoiceEngine") work as expected.
    IReadOnlyList<string>? AudioTracks = null,
    string Container = "mp4",
    int? ReplayBufferSeconds = null,
    int? VideoBitrate = null,
    string Quality = "medium",
    // gpu-screen-recorder requires -s WxH when -w focused is used. When
    // null the recorder queries `--list-monitors` and picks the largest
    // attached monitor; setting this skips that autodetect.
    string? CaptureSize = null,
    // Target encoded height. When set (e.g. 1080), the recorder scales the
    // captured frame to this height — width derived from the capture monitor's
    // aspect ratio so ultrawides don't get letterboxed. Null = record at the
    // monitor's native size. Ignored when <see cref="CaptureSize"/> is set
    // explicitly (the caller supplied a verbatim WxH).
    int? TargetHeight = null
);
