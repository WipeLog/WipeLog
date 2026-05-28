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

using Wipelog.Core.Data;

namespace Wipelog.App.ViewModels;

/// <summary>
/// Display helpers for the POV column of the clip table. Pure functions so
/// they can be unit-tested without spinning up Avalonia. The format is
/// "Name · Spec Class" with graceful degradation when any field is missing.
/// </summary>
public static class ClipPovFormatter
{
    /// <summary>"Aelara · Frost Mage" / "Aelara · Mage" / "Aelara" / "—".</summary>
    public static string FormatPov(string? playerClass, string? playerSpec, string? playerName)
    {
        var name = playerName ?? string.Empty;
        var classSpec = (playerClass, playerSpec) switch
        {
            (null or "", _) => string.Empty,
            (var c, null or "") => c!,
            (var c, var s) => $"{s} {c}",
        };
        return (string.IsNullOrEmpty(name), string.IsNullOrEmpty(classSpec)) switch
        {
            (true, true) => "—",
            (false, true) => name,
            (true, false) => classSpec,
            (false, false) => $"{name} · {classSpec}",
        };
    }

    /// <summary>Hex color string for the class swatch in the POV cell.</summary>
    public static string ClassColorHex(string? playerClass) => ClassColors.GetHex(playerClass);

    /// <summary>"+18" for an M+ key, empty string otherwise.</summary>
    public static string FormatKey(int? keystoneLevel) =>
        keystoneLevel is { } n ? $"+{n}" : string.Empty;
}
