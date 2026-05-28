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

using Wipelog.Core.Detection;

namespace Wipelog.Core.Recording;

/// <summary>
/// Optional hook the daemon uses to mirror recording lifecycle into out-of-process
/// observers (heartbeat status file, desktop notifications). Decoupled from
/// <see cref="IClipRecorderSink"/> because state tracking needs the start event,
/// not just the finalize event.
/// </summary>
public interface IRecordingLifecycleObserver
{
    Task OnRecordingStartedAsync(RecordingMetadata metadata, CancellationToken ct);
    Task OnRecordingFinalizedAsync(ClipFinalization clip, CancellationToken ct);
    Task OnRecordingErrorAsync(string message, Exception? exception, CancellationToken ct);
}
