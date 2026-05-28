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

namespace Wipelog.Core.Detection;

/// <summary>
/// Maps WoW raid difficulty IDs to filename slugs ("mythic") and sidecar
/// display names ("Mythic"). Unknown IDs fall back to a deterministic
/// `diff{N}` slug and `Difficulty {N}` display name.
/// </summary>
public static class DifficultyMap
{
    private static readonly Dictionary<int, (string Slug, string DisplayName)> Known = new()
    {
        { 14, ("normal", "Normal") },
        { 15, ("heroic", "Heroic") },
        { 16, ("mythic", "Mythic") },
        { 17, ("lfr", "LFR") },
    };

    public static string Slug(int? difficultyId)
    {
        if (difficultyId is null) return "unknown";
        return Known.TryGetValue(difficultyId.Value, out var info) ? info.Slug : $"diff{difficultyId.Value}";
    }

    public static string DisplayName(int? difficultyId)
    {
        if (difficultyId is null) return string.Empty;
        return Known.TryGetValue(difficultyId.Value, out var info) ? info.DisplayName : $"Difficulty {difficultyId.Value}";
    }

    /// <summary>
    /// Maps a raw WoW difficulty ID to a user-facing <see cref="RaidDifficulty"/>
    /// tier. Returns null for null or any unmapped ID, so callers can treat
    /// "unknown difficulty" as "don't filter".
    /// </summary>
    public static RaidDifficulty? ToRaidDifficulty(int? difficultyId) => difficultyId switch
    {
        14 => RaidDifficulty.Normal,
        15 => RaidDifficulty.Heroic,
        16 => RaidDifficulty.Mythic,
        17 => RaidDifficulty.LFR,
        _ => null,
    };
}
