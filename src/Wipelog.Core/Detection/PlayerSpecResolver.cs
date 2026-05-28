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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Data;

namespace Wipelog.Core.Detection;

/// <summary>
/// Identifies the recording player from a buffer of recent combat-log events.
///
/// Three paths, tried in order:
///   1. AFFILIATION_MINE flag — the WoW client stamps bit 0x1 of the lowest
///      4 bits of sourceFlags only on events the local player originates.
///      Any Player-* GUID seen with that bit set is the recording player.
///      This is canonical: it beats both the explicit hint and frequency.
///   2. Explicit <paramref name="playerName"/> hint — diacritic- and
///      case-insensitive match against sourceName ("Frostytanegl" matches
///      "Frostytånegl"). Used when the buffer has no MINE-flagged events
///      (older logs or stripped flags).
///   3. Frequency fallback — count Player-* sourceGUIDs in the buffer and
///      pick the most common. Last resort: in raids the tank typically
///      out-events the recording player, so this can be wrong.
///
/// With a candidate GUID, look up the matching <see cref="CombatantInfo"/> to
/// derive class + spec. If no <see cref="CombatantInfo"/> exists for the
/// candidate GUID, return identity with name+GUID set and class/spec null
/// rather than failing entirely (a warning is logged).
/// </summary>
public sealed class PlayerSpecResolver
{
    private const int AffiliationMine = 0x1;

    private readonly ILogger<PlayerSpecResolver> _logger;

    public PlayerSpecResolver()
        : this(NullLogger<PlayerSpecResolver>.Instance) { }

    public PlayerSpecResolver(ILogger<PlayerSpecResolver> logger)
    {
        _logger = logger;
    }

    public PlayerIdentity? Resolve(
        IReadOnlyList<CombatLogEvent> bufferedEvents,
        string? playerName)
    {
        if (bufferedEvents.Count == 0)
        {
            LogFailure(bufferedEvents, playerName);
            return null;
        }

        // Step 1: AFFILIATION_MINE — canonical client-stamped signal.
        var mineGuid = TryResolveByMineFlag(bufferedEvents, out var mineName);
        if (mineGuid is not null)
        {
            var identity = BuildIdentity(bufferedEvents, mineGuid, mineName);
            LogSuccess(identity, source: "AFFILIATION_MINE flag", topCount: null);
            return identity;
        }

        // Step 2: explicit-hint path. Diacritic- and case-insensitive match
        // against the configured name; on success, build identity for that GUID.
        if (!string.IsNullOrEmpty(playerName))
        {
            var hint = StringNormalization.FoldForCompare(playerName);
            string? matchedGuid = null;
            string? matchedName = null;
            foreach (var evt in bufferedEvents)
            {
                if (evt is not UnknownEvent unknown) continue;
                if (unknown.Fields.Count < 2) continue;
                var sourceGuid = unknown.Fields[0];
                var sourceName = unknown.Fields[1];
                if (string.IsNullOrEmpty(sourceGuid) || string.IsNullOrEmpty(sourceName)) continue;
                // Match against the full source name, but also against just
                // the name component before the first dash — so a hint of
                // "Frostytanegl" still matches a "Frostytånegl-Stormscale-EU"
                // cross-realm source name in older logs.
                var sourceNameOnly = sourceName.Split('-', 2)[0];
                if (StringNormalization.FoldForCompare(sourceName) == hint
                    || StringNormalization.FoldForCompare(sourceNameOnly) == hint)
                {
                    matchedGuid = sourceGuid;
                    matchedName = sourceName;
                    break;
                }
            }
            if (matchedGuid is not null)
            {
                var identity = BuildIdentity(bufferedEvents, matchedGuid, matchedName);
                LogSuccess(identity, source: "explicit name match", topCount: null);
                return identity;
            }
        }

        // Step 3: frequency auto-detect. Count Player-* source GUIDs over
        // the buffer. Last-resort heuristic — can pick the wrong raid member.
        var counts = new Dictionary<string, int>();
        var nameByGuid = new Dictionary<string, string>();
        foreach (var evt in bufferedEvents)
        {
            if (evt is not UnknownEvent unknown) continue;
            if (unknown.Fields.Count < 2) continue;
            var sourceGuid = unknown.Fields[0];
            if (!IsPlayerGuid(sourceGuid)) continue;
            counts.TryGetValue(sourceGuid, out var c);
            counts[sourceGuid] = c + 1;
            var sourceName = unknown.Fields[1];
            if (!string.IsNullOrEmpty(sourceName) && sourceName != "nil"
                && !nameByGuid.ContainsKey(sourceGuid))
            {
                nameByGuid[sourceGuid] = sourceName;
            }
        }
        if (counts.Count == 0)
        {
            LogFailure(bufferedEvents, playerName);
            return null;
        }

        var top = counts.OrderByDescending(kv => kv.Value).First();
        nameByGuid.TryGetValue(top.Key, out var topName);
        var resolved = BuildIdentity(bufferedEvents, top.Key, topName);
        LogSuccess(resolved, source: "frequency fallback (no MINE flag)", topCount: top.Value);
        return resolved;
    }

    /// <summary>
    /// Walks the buffer once looking for Player-* source GUIDs whose
    /// sourceFlags have AFFILIATION_MINE set. Returns the GUID with the most
    /// MINE-flagged events (tiebreak: latest event timestamp). Logs a
    /// warning if more than one distinct GUID has the bit set, since that's
    /// not supposed to happen.
    /// </summary>
    private string? TryResolveByMineFlag(
        IReadOnlyList<CombatLogEvent> buffer,
        out string? name)
    {
        name = null;
        Dictionary<string, (int Count, DateTimeOffset LastSeen, string? Name)>? hits = null;
        foreach (var evt in buffer)
        {
            if (evt is not UnknownEvent unknown) continue;
            if (unknown.Fields.Count < 3) continue;
            var sourceGuid = unknown.Fields[0];
            if (!IsPlayerGuid(sourceGuid)) continue;
            if (!TryParseSourceFlags(unknown.Fields[2], out var flags)) continue;
            if ((flags & AffiliationMine) == 0) continue;

            hits ??= new Dictionary<string, (int, DateTimeOffset, string?)>();
            hits.TryGetValue(sourceGuid, out var existing);
            var sourceName = unknown.Fields.Count > 1 ? unknown.Fields[1] : null;
            var capturedName = existing.Name
                ?? (string.IsNullOrEmpty(sourceName) || sourceName == "nil" ? null : sourceName);
            hits[sourceGuid] = (existing.Count + 1, evt.Timestamp, capturedName);
        }

        if (hits is null) return null;

        if (hits.Count > 1)
        {
            _logger.LogWarning(
                "Multiple GUIDs have AFFILIATION_MINE flag set ({Count}); picking by event count then recency. Candidates: [{Candidates}]",
                hits.Count,
                string.Join(", ", hits.Select(kv => $"{kv.Key}={kv.Value.Count}")));
        }

        var winner = hits
            .OrderByDescending(kv => kv.Value.Count)
            .ThenByDescending(kv => kv.Value.LastSeen)
            .First();
        name = winner.Value.Name;
        return winner.Key;
    }

    private static bool TryParseSourceFlags(string? hex, out int flags)
    {
        flags = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var span = hex.AsSpan().Trim();
        if (span.Length >= 2 && span[0] == '0' && (span[1] == 'x' || span[1] == 'X'))
        {
            span = span[2..];
        }
        if (span.IsEmpty) return false;
        return int.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out flags);
    }

    private PlayerIdentity BuildIdentity(
        IReadOnlyList<CombatLogEvent> buffer,
        string guid,
        string? name)
    {
        // Prefer the source name we already captured; fall back to scanning
        // for any prefixed event that names this GUID.
        var resolvedName = name ?? FindNameFor(buffer, guid);

        CombatantInfo? combatant = null;
        foreach (var evt in buffer)
        {
            if (evt is CombatantInfo ci && ci.PlayerGuid == guid)
            {
                combatant = ci;
                break;
            }
        }

        var (parsedName, realm, region) = PlayerIdentity.ParseFullName(resolvedName);

        if (combatant is null)
        {
            _logger.LogWarning(
                "Resolved player {Name} but could not determine spec (no COMBATANT_INFO for GUID {Guid})",
                resolvedName ?? "(unknown)", guid);
            return new PlayerIdentity(guid, parsedName, realm, region, null, null);
        }

        var spec = SpecMap.Lookup(combatant.SpecId);
        if (spec is null)
        {
            _logger.LogWarning(
                "Resolved player {Name} but spec ID {SpecId} is not in SpecMap",
                resolvedName ?? "(unknown)", combatant.SpecId);
            // Faction is still useful even without a recognised spec — PvP
            // win/loss resolution only needs the faction, not the class/spec.
            return new PlayerIdentity(guid, parsedName, realm, region, null, null, combatant.Faction);
        }

        return new PlayerIdentity(guid, parsedName, realm, region,
            spec.Value.Class, spec.Value.Spec, combatant.Faction);
    }

    private static string? FindNameFor(IReadOnlyList<CombatLogEvent> buffer, string guid)
    {
        foreach (var evt in buffer)
        {
            if (evt is not UnknownEvent unknown) continue;
            if (unknown.Fields.Count < 2) continue;
            if (unknown.Fields[0] != guid) continue;
            var name = unknown.Fields[1];
            if (!string.IsNullOrEmpty(name) && name != "nil") return name;
        }
        return null;
    }

    private static bool IsPlayerGuid(string? guid) =>
        !string.IsNullOrEmpty(guid) && guid.StartsWith("Player-", StringComparison.Ordinal);

    private void LogSuccess(PlayerIdentity identity, string source, int? topCount)
    {
        var sourceLabel = topCount is { } n ? $"{source} (top GUID: {n} events)" : source;
        _logger.LogInformation(
            "Resolved player {Name} ({Class} {Spec}) via {Source}",
            identity.Name ?? "(unknown)",
            identity.Class ?? "?",
            identity.Spec ?? "?",
            sourceLabel);
    }

    private void LogFailure(IReadOnlyList<CombatLogEvent> buffer, string? playerName)
    {
        var top = new Dictionary<string, int>();
        int parseable = 0;
        foreach (var evt in buffer)
        {
            if (evt is not UnknownEvent unknown) continue;
            if (unknown.Fields.Count < 1) continue;
            var guid = unknown.Fields[0];
            if (string.IsNullOrEmpty(guid)) continue;
            top.TryGetValue(guid, out var c);
            top[guid] = c + 1;
            if (unknown.Fields.Count >= 3 && TryParseSourceFlags(unknown.Fields[2], out _))
                parseable++;
        }
        var topList = string.Join(", ",
            top.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}={kv.Value}"));
        _logger.LogWarning(
            "Could not resolve recording player. Buffer had {Count} events ({Parseable} with parseable sourceFlags, none MINE-flagged), top source GUIDs: [{Top}]. --player-name was {Hint}.",
            buffer.Count,
            parseable,
            string.IsNullOrEmpty(topList) ? "(none)" : topList,
            string.IsNullOrEmpty(playerName) ? "not provided" : playerName);
    }
}
