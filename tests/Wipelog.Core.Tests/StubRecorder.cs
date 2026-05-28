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

using Wipelog.Core.Recording;

namespace Wipelog.Core.Tests;

internal sealed class StubRecorder : IRecorder
{
    public RecorderState State { get; private set; } = RecorderState.Idle;
    public List<string> StartedPaths { get; } = new();
    public int StopCount { get; private set; }
    public int SaveReplayCount { get; private set; }
    public Func<string, Task>? OnStart { get; init; }
    public Func<Task>? OnStop { get; init; }

    public event EventHandler<RecorderExitedUnexpectedlyEventArgs>? ExitedUnexpectedly;

    public void RaiseExitedUnexpectedly(int exitCode = -1, string stderrTail = "")
    {
        State = RecorderState.Failed;
        ExitedUnexpectedly?.Invoke(this, new RecorderExitedUnexpectedlyEventArgs(exitCode, stderrTail));
    }

    public async Task StartAsync(string outputPath, CancellationToken ct)
    {
        StartedPaths.Add(outputPath);
        State = RecorderState.Recording;
        if (OnStart is not null)
        {
            await OnStart(outputPath).ConfigureAwait(false);
        }
        else
        {
            // Simulate the recorder writing video bytes so the controller
            // has a temp file to rename on stop.
            await File.WriteAllTextAsync(outputPath, "fake video", ct).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        StopCount++;
        State = RecorderState.Idle;
        if (OnStop is not null)
        {
            await OnStop().ConfigureAwait(false);
        }
    }

    public Task SaveReplayAsync(CancellationToken ct)
    {
        SaveReplayCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
