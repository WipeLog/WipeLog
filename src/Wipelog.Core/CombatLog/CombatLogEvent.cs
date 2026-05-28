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

public abstract record CombatLogEvent(DateTimeOffset Timestamp);

public sealed record EncounterStart(
    DateTimeOffset Timestamp,
    int EncounterId,
    string EncounterName,
    int DifficultyId,
    int GroupSize,
    int InstanceId
) : CombatLogEvent(Timestamp);

public sealed record EncounterEnd(
    DateTimeOffset Timestamp,
    int EncounterId,
    string EncounterName,
    int DifficultyId,
    int GroupSize,
    bool Success
) : CombatLogEvent(Timestamp);

public sealed record ChallengeModeStart(
    DateTimeOffset Timestamp,
    string ZoneName,
    int InstanceId,
    int ChallengeModeId,
    int KeystoneLevel,
    int[] Affixes
) : CombatLogEvent(Timestamp);

public sealed record ChallengeModeEnd(
    DateTimeOffset Timestamp,
    int InstanceId,
    bool Success,
    int KeystoneLevel,
    TimeSpan Duration
) : CombatLogEvent(Timestamp);

public sealed record UnitDied(
    DateTimeOffset Timestamp,
    string DestGuid,
    string? DestName,
    // WoW's trailing `unconsciousOnDeath` flag on UNIT_DIED. True for Feign
    // Death and a handful of raid mechanics that incapacitate without killing
    // — these shouldn't count as deaths in the timeline.
    bool IsUnconscious = false
) : CombatLogEvent(Timestamp);

public sealed record CombatLogVersion(
    DateTimeOffset Timestamp,
    int Version,
    bool AdvancedLogging
) : CombatLogEvent(Timestamp);

public sealed record CombatantInfo(
    DateTimeOffset Timestamp,
    string PlayerGuid,
    int SpecId,
    int? Faction = null
) : CombatLogEvent(Timestamp);

public sealed record DamageEvent(
    DateTimeOffset Timestamp,
    string? SourceGuid,
    string? SourceName,
    string DestGuid,
    string DestName,
    int? SpellId,
    string? SpellName,
    long Amount,
    // Destination unit's HP at the moment of the hit. Lifted from positions
    // [2]/[3] of the 17-field advanced-logging block when present, null
    // otherwise. Used to surface "Wiped at X%" on raid clips.
    long? DestCurrentHp = null,
    long? DestMaxHp = null
) : CombatLogEvent(Timestamp);

/// <summary>
/// Emitted by the client at the start of an Arena or Solo Shuffle match.
/// <para>
/// Documented log shape (subject to real-log iteration):
/// <c>ARENA_MATCH_START,instanceID,?,bracket,matchType</c>. The
/// <see cref="Bracket"/> field is the disambiguator for Solo Shuffle vs
/// regular Arena ("Rated Solo Shuffle" / "Arena3v3" / "Skirmish" / etc.).
/// </para>
/// </summary>
public sealed record ArenaMatchStart(
    DateTimeOffset Timestamp,
    int InstanceId,
    string Bracket,
    string MatchType
) : CombatLogEvent(Timestamp);

/// <summary>
/// Emitted at the end of an Arena or Solo Shuffle match.
/// <c>WinningFaction</c> is the in-game faction id (0=Horde, 1=Alliance,
/// historically); compare against the recording player's faction to derive
/// win/loss for the clip filename.
/// </summary>
public sealed record ArenaMatchEnd(
    DateTimeOffset Timestamp,
    int WinningFaction,
    TimeSpan Duration
) : CombatLogEvent(Timestamp);

/// <summary>
/// Emitted when the player crosses a zone boundary. Used by the encounter
/// state machine to detect Battleground entry/exit (no dedicated BG event
/// exists in the log).
/// </summary>
public sealed record ZoneChange(
    DateTimeOffset Timestamp,
    int ZoneId,
    string ZoneName,
    int DifficultyId
) : CombatLogEvent(Timestamp);

public sealed record UnknownEvent(
    DateTimeOffset Timestamp,
    string EventName,
    IReadOnlyList<string> Fields
) : CombatLogEvent(Timestamp);
