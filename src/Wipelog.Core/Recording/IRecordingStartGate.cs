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

/// <summary>
/// Hook invoked at the top of every recording start so a downstream policy can
/// free disk space before the recorder writes a new clip. Defined in Core (no
/// DB/Library dependency); the daemon implements it over the clip library.
/// Implementations must be best-effort — <see cref="RecorderController"/>
/// swallows any exception so capacity management can never block a recording.
/// </summary>
public interface IRecordingStartGate
{
    Task EnsureCapacityAsync(CancellationToken ct);
}
