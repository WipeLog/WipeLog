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

namespace Wipelog.App.ViewModels;

/// <summary>
/// Maps the rail's <see cref="ContentTypeFilter"/> to one of three explicit
/// column sets the table can render. Three separate configurations beat a
/// single dynamic column collection — the per-variant set is small and the
/// branching reads like the table looks.
/// </summary>
public enum ClipsTableVariant
{
    /// <summary>Dungeon | Key | Result | Duration | POV</summary>
    MythicPlus,
    /// <summary>Encounter | Difficulty | Result | Duration | POV</summary>
    Raid,
    /// <summary>Content | Title | Result | Duration | POV</summary>
    All,
}

public static class ClipsTableColumnSet
{
    public static ClipsTableVariant For(ContentTypeFilter filter) => filter switch
    {
        ContentTypeFilter.MythicPlus => ClipsTableVariant.MythicPlus,
        ContentTypeFilter.Raid => ClipsTableVariant.Raid,
        _ => ClipsTableVariant.All,
    };
}
