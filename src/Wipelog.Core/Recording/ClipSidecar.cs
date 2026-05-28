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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wipelog.Core.Recording;

internal sealed record ClipSidecar(
    int Version,
    string ContentType,
    string DisplayName,
    string? Difficulty,
    int? EncounterId,
    int? KeystoneLevel,
    bool Success,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    string VideoFile,
    string? PlayerName,
    string? PlayerRealm,
    string? PlayerRegion,
    string? PlayerClass,
    string? PlayerSpec,
    string? InstanceName,
    string? DungeonName,
    string? ArenaBracket = null,
    // Seconds of pre-encounter footage prepended to the start of VideoFile
    // when the daemon was run with --preroll-seconds. Null/absent for clips
    // recorded without pre-roll. Encounter T=0 sits at PreRollSeconds inside
    // the video file; the player VM offsets timeline events accordingly.
    double? PreRollSeconds = null,
    // Seconds of post-encounter footage tacked onto the end of VideoFile
    // when the daemon was run with --postroll-seconds. Recording was held
    // open this long after the stop signal (kill/wipe/zone-change) so the
    // user can see the immediate aftermath. Null/absent for clips recorded
    // without post-roll.
    double? PostRollSeconds = null,
    // Final boss HP percent (0..100) at the moment of the wipe. Raid wipes
    // only — null on kills, on non-raid content, and on multi-boss raid
    // encounters whose ENCOUNTER_START name doesn't name a single unit.
    double? BossHpPercent = null,
    // M+ key upgrade level: 1/2/3 = +1/+2/+3 chest for an in-time finish,
    // 0 = depleted by time, null = non-M+ content OR the dungeon's par
    // time is unknown to DungeonTimers.
    int? KeyUpgradeLevel = null);

internal static class ClipSidecarJson
{
    // UnsafeRelaxedJsonEscaping keeps non-ASCII chars (e.g. "Frostytånegl")
    // readable in the file instead of escaped to \uXXXX. The "unsafe" name
    // refers to HTML-context use; for filesystem JSON it's the right default.
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
