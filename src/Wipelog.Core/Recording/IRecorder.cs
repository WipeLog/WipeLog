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

public interface IRecorder : IAsyncDisposable
{
    RecorderState State { get; }

    /// <summary>
    /// Fires when the underlying recorder process exits while the recorder was
    /// in <see cref="RecorderState.Recording"/> state — i.e., the process died
    /// on its own rather than as a response to <see cref="StopAsync"/>. After
    /// this fires <see cref="State"/> is <see cref="RecorderState.Failed"/>;
    /// the controller should finalize the current clip as a failure rather
    /// than expecting a clean temp file.
    /// </summary>
    event EventHandler<RecorderExitedUnexpectedlyEventArgs>? ExitedUnexpectedly;

    Task StartAsync(string outputPath, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task SaveReplayAsync(CancellationToken ct);
}

public sealed class RecorderExitedUnexpectedlyEventArgs : EventArgs
{
    public RecorderExitedUnexpectedlyEventArgs(int exitCode, string stderrTail)
    {
        ExitCode = exitCode;
        StderrTail = stderrTail;
    }

    public int ExitCode { get; }

    /// <summary>Last buffered stderr lines from the recorder, newest last. May be empty.</summary>
    public string StderrTail { get; }
}
