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
using Wipelog.Core.Recording;
using Wipelog.Daemon.Notifications;

namespace Wipelog.Daemon.Status;

internal sealed class DaemonLifecycleObserver : IRecordingLifecycleObserver
{
    private readonly HeartbeatState _state;
    private readonly INotificationService _notifications;

    public DaemonLifecycleObserver(HeartbeatState state, INotificationService notifications)
    {
        _state = state;
        _notifications = notifications;
    }

    public Task OnRecordingStartedAsync(RecordingMetadata metadata, CancellationToken ct)
    {
        _state.MarkRecordingStarted(metadata);
        return Task.CompletedTask;
    }

    public async Task OnRecordingFinalizedAsync(ClipFinalization clip, CancellationToken ct)
    {
        _state.MarkRecordingFinalized();
        await _notifications.NotifyClipSavedAsync(clip, ct).ConfigureAwait(false);
    }

    public async Task OnRecordingErrorAsync(string message, Exception? exception, CancellationToken ct)
    {
        _state.MarkError(message);
        await _notifications.NotifyErrorAsync("Wipelog error", message, ct).ConfigureAwait(false);
    }

    // Capture failures also arrive via OnRecordingErrorAsync (toast + tooltip
    // LastError); these two only maintain the sticky sidebar warning.
    public Task OnRecorderUnhealthyAsync(string message, CancellationToken ct)
    {
        _state.SetRecorderError(message);
        return Task.CompletedTask;
    }

    public Task OnRecorderHealthyAsync(CancellationToken ct)
    {
        _state.SetRecorderError(null);
        return Task.CompletedTask;
    }
}
