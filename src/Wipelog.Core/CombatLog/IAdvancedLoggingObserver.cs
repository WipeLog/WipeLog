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

namespace Wipelog.Core.CombatLog;

/// <summary>
/// Receives every <see cref="CombatLogVersion"/> the daemon parses out of
/// the live log. Implementations are expected to be cheap — they run on
/// the recorder event loop and any heavy work should be dispatched off-
/// thread by the implementation.
///
/// <para>
/// Wired so the daemon can detect Advanced Combat Logging being toggled off
/// (`ADVANCED_LOG_ENABLED=0` in the log header) and surface a warning to
/// the GUI. ACL controls whether damage events include the 17-field
/// advanced block — without it, kill-blow attribution, boss HP%, and the
/// raid-comp chip strip all break.
/// </para>
/// </summary>
public interface IAdvancedLoggingObserver
{
    void OnCombatLogVersion(CombatLogVersion ev);
}
