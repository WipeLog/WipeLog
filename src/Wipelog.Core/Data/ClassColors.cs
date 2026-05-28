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

namespace Wipelog.Core.Data;

/// <summary>
/// Canonical Blizzard class colors. Used to color the POV cell in the clip
/// table so "all my Mage runs" is a visual scan rather than a text scan.
/// The hex values are fixed by Blizzard and don't change between expansions.
/// </summary>
public static class ClassColors
{
    public static readonly IReadOnlyDictionary<string, string> Hex =
        new Dictionary<string, string>
        {
            { "Death Knight", "#C41E3A" },
            { "Demon Hunter", "#A330C9" },
            { "Druid",        "#FF7C0A" },
            { "Evoker",       "#33937F" },
            { "Hunter",       "#AAD372" },
            { "Mage",         "#3FC7EB" },
            { "Monk",         "#00FF98" },
            { "Paladin",      "#F48CBA" },
            { "Priest",       "#FFFFFF" },
            { "Rogue",        "#FFF468" },
            { "Shaman",       "#0070DD" },
            { "Warlock",      "#8788EE" },
            { "Warrior",      "#C69B6D" },
        };

    /// <summary>
    /// Hex color for the given class name, or a neutral grey for unknown /
    /// missing classes. Never returns null so XAML brushes can bind directly.
    /// </summary>
    public static string GetHex(string? className) =>
        className is not null && Hex.TryGetValue(className, out var hex)
            ? hex
            : "#80848e";
}
