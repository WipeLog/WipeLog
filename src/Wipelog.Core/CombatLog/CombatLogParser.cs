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
using Wipelog.Core.Data;
using Wipelog.Core.Time;

namespace Wipelog.Core.CombatLog;

public sealed class CombatLogParser
{
    // Year-less, used by older clients — we prepend the clock year before parsing.
    private static readonly string[] LegacyTimestampFormats =
    {
        "yyyy/M/d HH:mm:ss.fff",
        "yyyy/M/d HH:mm:ss.ff",
        "yyyy/M/d HH:mm:ss.f",
    };

    // Modern WoW retail (12.x+) writes the full year inline and uses 4-digit
    // fractional seconds; we keep the 3-digit form too as a safety net.
    private static readonly string[] InlineYearTimestampFormats =
    {
        "M/d/yyyy HH:mm:ss.ffff",
        "M/d/yyyy HH:mm:ss.fff",
        "M/d/yyyy HH:mm:ss.fffffff",
    };

    private readonly IClock _clock;

    public CombatLogParser(IClock clock)
    {
        _clock = clock;
    }

    public CombatLogEvent? TryParseLine(ReadOnlySpan<char> line)
    {
        try
        {
            line = line.Trim();
            if (line.IsEmpty)
            {
                return null;
            }

            if (!TrySplitTimestampAndBody(line, out var timestampSpan, out var bodySpan))
            {
                return null;
            }

            if (!TryParseTimestamp(timestampSpan, out var timestamp))
            {
                return null;
            }

            var fields = CombatLogFieldSplitter.Split(bodySpan);
            if (fields.Count == 0 || fields[0].Length == 0)
            {
                return null;
            }

            string eventName = fields[0];
            return eventName switch
            {
                "COMBAT_LOG_VERSION" => BuildCombatLogVersion(timestamp, fields),
                "ENCOUNTER_START" => BuildEncounterStart(timestamp, fields),
                "ENCOUNTER_END" => BuildEncounterEnd(timestamp, fields),
                "CHALLENGE_MODE_START" => BuildChallengeModeStart(timestamp, fields),
                "CHALLENGE_MODE_END" => BuildChallengeModeEnd(timestamp, fields),
                "ARENA_MATCH_START" => BuildArenaMatchStart(timestamp, fields),
                "ARENA_MATCH_END" => BuildArenaMatchEnd(timestamp, fields),
                "ZONE_CHANGE" => BuildZoneChange(timestamp, fields),
                "UNIT_DIED" => BuildUnitDied(timestamp, fields),
                "COMBATANT_INFO" => BuildCombatantInfo(timestamp, fields),
                "SPELL_DAMAGE" or "SPELL_PERIODIC_DAMAGE" or "RANGE_DAMAGE"
                    or "SPELL_BUILDING_DAMAGE" => BuildDamageEvent(timestamp, fields, hasSpellPrefix: true),
                "SWING_DAMAGE" or "SWING_DAMAGE_LANDED"
                    => BuildDamageEvent(timestamp, fields, hasSpellPrefix: false),
                _ => new UnknownEvent(timestamp, eventName, fields.GetRange(1, fields.Count - 1)),
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySplitTimestampAndBody(
        ReadOnlySpan<char> line,
        out ReadOnlySpan<char> timestamp,
        out ReadOnlySpan<char> body)
    {
        timestamp = default;
        body = default;

        int firstWhitespace = -1;
        bool seenNonWhitespace = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                if (seenNonWhitespace)
                {
                    firstWhitespace = i;
                    break;
                }
            }
            else
            {
                seenNonWhitespace = true;
            }
        }

        if (firstWhitespace < 0)
        {
            return false;
        }

        // Skip the date part (M/d) plus the first whitespace, then look for the
        // run of whitespace that separates the full timestamp from the body.
        int searchFrom = firstWhitespace + 1;
        int boundary = -1;
        for (int i = searchFrom; i < line.Length - 1; i++)
        {
            if (char.IsWhiteSpace(line[i]) && char.IsWhiteSpace(line[i + 1]))
            {
                boundary = i;
                break;
            }
            if (line[i] == '\t')
            {
                boundary = i;
                break;
            }
        }

        if (boundary < 0)
        {
            return false;
        }

        timestamp = line[..boundary].Trim();
        int bodyStart = boundary;
        while (bodyStart < line.Length && char.IsWhiteSpace(line[bodyStart]))
        {
            bodyStart++;
        }
        body = line[bodyStart..];
        return !timestamp.IsEmpty && !body.IsEmpty;
    }

    private bool TryParseTimestamp(ReadOnlySpan<char> span, out DateTimeOffset timestamp)
    {
        timestamp = default;
        var now = _clock.Now;

        int slashes = 0;
        foreach (var c in span)
        {
            if (c == '/') slashes++;
        }

        DateTime dt;
        if (slashes >= 2)
        {
            // Modern format already carries the year — parse as-is.
            if (!DateTime.TryParseExact(
                    span,
                    InlineYearTimestampFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out dt))
            {
                return false;
            }
        }
        else
        {
            // Legacy year-less format. Prepend the clock year and roll back if
            // the parsed month is in the future relative to "now".
            var withYear = $"{now.Year}/{span}";
            if (!DateTime.TryParseExact(
                    withYear,
                    LegacyTimestampFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out dt))
            {
                return false;
            }
            if (dt.Month > now.Month)
            {
                dt = dt.AddYears(-1);
            }
        }

        // Use the offset that was in effect *at the parsed moment*, not the
        // clock's current offset. This makes timestamps DST-correct: a November
        // log line parsed during May's DST gets November's offset.
        var local = DateTime.SpecifyKind(dt, DateTimeKind.Unspecified);
        var offset = _clock.Zone.GetUtcOffset(local);
        timestamp = new DateTimeOffset(local, offset);
        return true;
    }

    private static CombatLogEvent? BuildCombatLogVersion(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 2 || !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            return null;
        }

        bool advancedLogging = false;
        for (int i = 2; i + 1 < fields.Count; i += 2)
        {
            if (fields[i] == "ADVANCED_LOG_ENABLED" && fields[i + 1] == "1")
            {
                advancedLogging = true;
                break;
            }
        }

        return new CombatLogVersion(timestamp, version, advancedLogging);
    }

    private static CombatLogEvent? BuildEncounterStart(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 6) return null;
        if (!TryInt(fields[1], out var id)) return null;
        if (!TryInt(fields[3], out var difficulty)) return null;
        if (!TryInt(fields[4], out var groupSize)) return null;
        if (!TryInt(fields[5], out var instanceId)) return null;
        return new EncounterStart(timestamp, id, fields[2], difficulty, groupSize, instanceId);
    }

    private static CombatLogEvent? BuildEncounterEnd(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 6) return null;
        if (!TryInt(fields[1], out var id)) return null;
        if (!TryInt(fields[3], out var difficulty)) return null;
        if (!TryInt(fields[4], out var groupSize)) return null;
        bool success = fields[5] == "1";
        return new EncounterEnd(timestamp, id, fields[2], difficulty, groupSize, success);
    }

    private static CombatLogEvent? BuildChallengeModeStart(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 6) return null;
        if (!TryInt(fields[2], out var instanceId)) return null;
        if (!TryInt(fields[3], out var challengeModeId)) return null;
        if (!TryInt(fields[4], out var keystoneLevel)) return null;
        if (!TryParseIntArray(fields[5], out var affixes)) return null;
        return new ChallengeModeStart(timestamp, fields[1], instanceId, challengeModeId, keystoneLevel, affixes);
    }

    private static CombatLogEvent? BuildChallengeModeEnd(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 5) return null;
        if (!TryInt(fields[1], out var instanceId)) return null;
        bool success = fields[2] == "1";
        if (!TryInt(fields[3], out var keystoneLevel)) return null;
        if (!long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationMs)) return null;
        return new ChallengeModeEnd(timestamp, instanceId, success, keystoneLevel, TimeSpan.FromMilliseconds(durationMs));
    }

    // ARENA_MATCH_START,instanceID,?,bracket,matchType — field offsets confirmed
    // against documented WoW log format. Real-log iteration may reveal a different
    // shape across client versions; bracket and matchType are intentionally kept
    // as raw strings so the encounter detector can fold "Rated Solo Shuffle" /
    // "Arena3v3" / "Skirmish" / "War Game" however the live client emits them.
    private static CombatLogEvent? BuildArenaMatchStart(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 5) return null;
        if (!TryInt(fields[1], out var instanceId)) return null;
        return new ArenaMatchStart(timestamp, instanceId, fields[3], fields[4]);
    }

    // ARENA_MATCH_END,winningFaction,matchDurationSeconds,...
    private static CombatLogEvent? BuildArenaMatchEnd(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 3) return null;
        if (!TryInt(fields[1], out var winningFaction)) return null;
        if (!long.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var durationSec)) return null;
        return new ArenaMatchEnd(timestamp, winningFaction, TimeSpan.FromSeconds(durationSec));
    }

    // ZONE_CHANGE,zoneId,zoneName,difficultyId
    private static CombatLogEvent? BuildZoneChange(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 4) return null;
        if (!TryInt(fields[1], out var zoneId)) return null;
        if (!TryInt(fields[3], out var difficultyId)) return null;
        return new ZoneChange(timestamp, zoneId, fields[2], difficultyId);
    }

    // Spec ID lives in the COMBATANT_INFO field block somewhere between
    // index 22 and 28 depending on patch (12.x has it at 25). Rather than
    // hardcode, scan the candidate range and pick the first int that maps
    // to a known spec — robust against minor schema shifts.
    private static CombatLogEvent? BuildCombatantInfo(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        if (fields.Count < 26) return null;
        string playerGuid = fields[1];
        if (string.IsNullOrEmpty(playerGuid)) return null;

        // Field [2] is the player's faction (0=Horde, 1=Alliance) per
        // documented log format. Stays null when malformed so the rest of the
        // record still resolves.
        int? faction = TryInt(fields[2], out var f) ? f : (int?)null;

        for (int i = 23; i <= 30 && i < fields.Count; i++)
        {
            if (!TryInt(fields[i], out var candidate)) continue;
            if (SpecMap.Known.ContainsKey(candidate))
            {
                return new CombatantInfo(timestamp, playerGuid, candidate, faction);
            }
        }
        return null;
    }

    // Damage events have three layouts depending on event family and whether
    // ADVANCED_LOG_ENABLED is on: standard prefix (8) [+ spell prefix (3)]
    // [+ advanced params (17)] + damage suffix (10). The suffix always sits at
    // the tail and starts with the amount field, so we count from the end —
    // that's stable regardless of the advanced-logging block in the middle.
    private const int DamageSuffixFields = 10;

    private static CombatLogEvent? BuildDamageEvent(
        DateTimeOffset timestamp,
        List<string> fields,
        bool hasSpellPrefix)
    {
        int prefixFields = 1 /* event */ + 8 /* std prefix */ + (hasSpellPrefix ? 3 : 0);
        if (fields.Count < prefixFields + DamageSuffixFields) return null;

        string destGuid = fields[5];
        string? destName = NormalizeNil(fields[6]);
        if (string.IsNullOrEmpty(destGuid) || destName is null) return null;

        string? sourceGuid = NormalizeGuid(fields[1]);
        string? sourceName = NormalizeNil(fields[2]);

        int? spellId = null;
        string? spellName = null;
        if (hasSpellPrefix)
        {
            if (TryInt(fields[9], out var sid)) spellId = sid;
            spellName = NormalizeNil(fields[10]);
        }

        var amountField = fields[fields.Count - DamageSuffixFields];
        if (!long.TryParse(amountField, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
            return null;

        // Advanced-logging block (17 fields) sits between the prefix and the
        // damage suffix when ADVANCED_LOG_ENABLED=1. Within that block:
        //   [0] infoGUID, [1] ownerGUID, [2] currentHP, [3] maxHP, ...
        // When ACL is off the block is absent and we just leave HP null.
        long? destCurrentHp = null;
        long? destMaxHp = null;
        int suffixStart = fields.Count - DamageSuffixFields;
        int advancedBlockSize = suffixStart - prefixFields;
        if (advancedBlockSize >= 17)
        {
            if (long.TryParse(fields[prefixFields + 2], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var curHp) && curHp >= 0)
            {
                destCurrentHp = curHp;
            }
            if (long.TryParse(fields[prefixFields + 3], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var maxHp) && maxHp > 0)
            {
                destMaxHp = maxHp;
            }
        }

        return new DamageEvent(
            timestamp, sourceGuid, sourceName, destGuid, destName, spellId, spellName, amount,
            destCurrentHp, destMaxHp);
    }

    private static CombatLogEvent? BuildUnitDied(
        DateTimeOffset timestamp,
        List<string> fields)
    {
        // Standard combat-log prefix: source GUID, source name, source flags,
        // source raid flags, dest GUID, dest name, dest flags, dest raid flags.
        // Event name occupies index 0, so dest GUID is at index 5, dest name at index 6.
        if (fields.Count < 7) return null;
        string destGuid = fields[5];
        string? destName = NormalizeNil(fields[6]);
        // fields[9] is WoW's `unconsciousOnDeath` flag (1 = Feign Death / raid
        // unconscious mechanic, 0 = real death). Absent on pre-Dragonflight logs.
        bool isUnconscious = fields.Count >= 10
            && TryInt(fields[9], out var unconscious)
            && unconscious == 1;
        return new UnitDied(timestamp, destGuid, destName, isUnconscious);
    }

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryParseIntArray(string field, out int[] values)
    {
        values = Array.Empty<int>();
        if (field.Length < 2 || field[0] != '[' || field[^1] != ']')
        {
            return false;
        }

        var inner = field.AsSpan(1, field.Length - 2);
        if (inner.IsEmpty)
        {
            return true;
        }

        var parts = inner.ToString().Split(',');
        var parsed = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i]))
            {
                return false;
            }
        }
        values = parsed;
        return true;
    }

    private static string? NormalizeNil(string field) =>
        field.Length == 0 || field == "nil" ? null : field;

    private static string? NormalizeGuid(string field) =>
        field.Length == 0 || field == "0000000000000000" || field == "nil" ? null : field;
}
