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

namespace Wipelog.App.Services;

/// <summary>
/// Wraps the systemd user service so both the tray icon and the in-app
/// status popover share a single implementation (and tests can substitute
/// a fake).
/// </summary>
public interface IDaemonLifecycleController
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task RestartAsync(CancellationToken ct);
    Task OpenJournalAsync(CancellationToken ct);
}
