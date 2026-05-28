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

using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace Wipelog.Core.Detection;

/// <summary>
/// Raid difficulty tier as exposed to the user-facing filter UI. Distinct from
/// the raw WoW difficulty IDs (14/15/16/17) — see <see cref="DifficultyMap"/>
/// for the mapping. Serialized as a string ("Heroic") so the on-disk
/// preferences file stays human-readable.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RaidDifficulty>))]
public enum RaidDifficulty
{
    LFR,
    Normal,
    Heroic,
    Mythic,
}

/// <summary>
/// User-tweakable content filters consulted by <see cref="EncounterDetector"/>
/// before it starts a recording. Defaults record everything (current behavior);
/// users opt into being more restrictive. Persisted in
/// <c>~/.config/wipelog/preferences.json</c> under <c>recordingFilters</c> and
/// read by the daemon at startup only — changes take effect on the next service
/// restart/reinstall.
/// </summary>
public sealed record RecordingFilters
{
    [JsonPropertyName("recordRaids")]
    public bool RecordRaids { get; init; } = true;

    [JsonPropertyName("recordMythicPlus")]
    public bool RecordMythicPlus { get; init; } = true;

    [JsonPropertyName("recordBattlegrounds")]
    public bool RecordBattlegrounds { get; init; } = true;

    /// <summary>
    /// Raid difficulties to record (only relevant when <see cref="RecordRaids"/>
    /// is true). An empty list means "no difficulty filter" — every difficulty is
    /// recorded. Difficulty IDs that don't map to a known tier are always
    /// recorded. Modeled as a list (not a set) because System.Text.Json can't
    /// deserialize into <c>IReadOnlySet&lt;T&gt;</c>; membership is still
    /// order-insensitive via <see cref="System.Linq.Enumerable.Contains"/>.
    /// </summary>
    [JsonPropertyName("raidDifficulties")]
    public IReadOnlyList<RaidDifficulty> RaidDifficulties { get; init; } = new[]
    {
        RaidDifficulty.LFR,
        RaidDifficulty.Normal,
        RaidDifficulty.Heroic,
        RaidDifficulty.Mythic,
    };

    /// <summary>
    /// Minimum keystone level to record (only relevant when
    /// <see cref="RecordMythicPlus"/> is true). Runs below this are skipped.
    /// </summary>
    [JsonPropertyName("minKeystoneLevel")]
    public int MinKeystoneLevel { get; init; } = 2;

    /// <summary>
    /// Decide whether a raid encounter at the given WoW difficulty ID should be
    /// recorded. <paramref name="reason"/> carries a human-readable skip
    /// explanation when the result is false.
    /// </summary>
    public bool AllowsRaid(int? difficultyId, out string reason)
    {
        if (!RecordRaids)
        {
            reason = "RecordRaids is disabled";
            return false;
        }

        // Unknown / unmapped difficulty IDs aren't filtered — we'd rather record
        // an unexpected tier than silently drop it. An empty set is also treated
        // as "no filter" so the user can't accidentally disable all raids by
        // clearing the chips while the toggle is on.
        var tier = DifficultyMap.ToRaidDifficulty(difficultyId);
        if (tier is { } known && RaidDifficulties.Count > 0 && !RaidDifficulties.Contains(known))
        {
            reason = $"RaidDifficulties does not include {known}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Decide whether a Mythic+ run at the given keystone level should be
    /// recorded. A null level (unknown) is always recorded.
    /// </summary>
    public bool AllowsMythicPlus(int? keystoneLevel, out string reason)
    {
        if (!RecordMythicPlus)
        {
            reason = "RecordMythicPlus is disabled";
            return false;
        }

        if (keystoneLevel is { } level && level < MinKeystoneLevel)
        {
            reason = string.Format(
                CultureInfo.InvariantCulture,
                "keystone +{0} below minimum +{1}",
                level,
                MinKeystoneLevel);
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Decide whether a battleground should be recorded.</summary>
    public bool AllowsBattleground(out string reason)
    {
        if (!RecordBattlegrounds)
        {
            reason = "RecordBattlegrounds is disabled";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
