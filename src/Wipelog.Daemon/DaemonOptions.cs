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

namespace Wipelog.Daemon;

internal sealed record DaemonOptions(
    string? LogPath,
    string? LogDirectory,
    string LogPattern,
    string OutputDirectory,
    string DatabasePath,
    string Executable,
    string Window,
    int Framerate,
    string Codec,
    IReadOnlyList<string> AudioTracks,
    string Container,
    string Quality,
    // Target encoded height: 720, 1080, 1440. Null = record at the capture
    // monitor's native resolution. Parsed from --video-resolution; null when
    // the user picked "source" / left it blank.
    int? TargetHeight,
    string? PlayerName,
    // When set, daemon launches a second gpu-screen-recorder in replay-buffer
    // mode that keeps the last N seconds in RAM. Each ENCOUNTER_START flushes
    // that buffer to disk and the controller stitches it onto the front of
    // the finalized clip so the user sees the pre-pull seconds. Null/0 = off.
    int? PreRollSeconds,
    // When set, the recorder controller holds each clip open for N extra
    // seconds after the encounter's stop signal so the saved video captures
    // the immediate aftermath (kill cam, wipe panic, key chest). Plain
    // Task.Delay between StopRecording and recorder.StopAsync — no second
    // recorder process required. Null/0 = off.
    int? PostRollSeconds);
