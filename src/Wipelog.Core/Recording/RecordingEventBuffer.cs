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

using Wipelog.Core.CombatLog;
using Wipelog.Core.Data;

namespace Wipelog.Core.Recording;

/// <summary>
/// Collects timeline-relevant combat-log events during a recording and
/// produces a <see cref="TimelineEventDocument"/> at stop. Stores
/// <see cref="UnitDied"/> and <see cref="EncounterStart"/> records in full
/// plus a rolling window of recent <see cref="DamageEvent"/>s keyed by
/// destination GUID, used to attribute the killing blow on each death.
///
/// Bounded: damage events older than the lookback window are evicted on
/// every Observe call, and a hard cap of 50 000 damage events stops a
/// pathological session from running away. Deaths and boss starts are
/// essential and never evicted.
/// </summary>
public sealed class RecordingEventBuffer
{
    private const int MaxDamageEvents = 50_000;

    private readonly DateTimeOffset _recordingStart;
    private readonly TimeSpan _killingBlowLookback;
    private readonly List<DeathRecord> _deaths = new();
    private readonly List<EncounterStart> _bossStarts = new();
    private readonly List<EncounterEnd> _bossEnds = new();
    // GUID → most recently observed spec ID. CombatantInfo can repeat for the
    // same player across nested encounters (e.g. an M+ key whose bosses each
    // re-emit it); last write wins so a mid-run spec change still reflects.
    private readonly Dictionary<string, int> _combatants = new();

    /// <summary>
    /// A UnitDied plus the kill-blow attribution captured at observation
    /// time. We attribute eagerly (rather than at Build) because incoming
    /// damage events to other targets later in the session evict the dying
    /// player's bucket via <see cref="EvictExpired"/>, so by Build time the
    /// damage data we need is long gone.
    /// </summary>
    private sealed record DeathRecord(
        UnitDied Death, string? Source, string? Spell, long? Amount);
    // Damage events keyed by destination GUID (the unit being hit). Each
    // list is kept in arrival order so we can pop expired entries from the
    // front. We only ever look up by destGuid for kill-blow attribution.
    private readonly Dictionary<string, LinkedList<DamageEvent>> _damageByTarget = new();
    private int _damageCount;

    // Last (currentHp, maxHp) we saw for any damage event whose dest name
    // matched the *current* encounter's boss name. Used at finalize to
    // report "Wiped at X%" for raid wipes. Multi-boss raid fights (e.g.
    // "Council of Dreams") whose ENCOUNTER_START name doesn't match a unit
    // never populate this — they stay null and the UI hides the field.
    private (long Current, long Max, DateTimeOffset At)? _lastBossHp;
    private string? _activeBossName;

    public RecordingEventBuffer(
        DateTimeOffset recordingStart,
        TimeSpan killingBlowLookback,
        string? bossName = null)
    {
        _recordingStart = recordingStart;
        _killingBlowLookback = killingBlowLookback;
        _activeBossName = bossName;
    }

    public void Observe(CombatLogEvent ev)
    {
        switch (ev)
        {
            case UnitDied died:
                // Feign Death and raid "unconscious" mechanics emit UNIT_DIED
                // but the player isn't actually dead — drop them entirely
                // (no kill-blow attribution either; it'd just steal damage
                // from the next real death of the same target).
                if (died.IsUnconscious) break;
                // Capture kill-blow attribution NOW — the damage bucket
                // for this target has the relevant events while it's still
                // within the lookback window. By Build() time, ongoing
                // damage to other targets will have evicted them.
                var (source, spell, amount) = FindKillingBlow(died);
                _deaths.Add(new DeathRecord(died, source, spell, amount));
                break;
            case EncounterStart start:
                _bossStarts.Add(start);
                // Fall back to the first observed encounter name if the
                // controller didn't pass one explicitly — keeps the buffer
                // useful for older call sites + tests.
                _activeBossName ??= start.EncounterName;
                break;
            case EncounterEnd end:
                _bossEnds.Add(end);
                break;
            case CombatantInfo ci:
                _combatants[ci.PlayerGuid] = ci.SpecId;
                break;
            case DamageEvent dmg:
                AddDamage(dmg);
                TrackBossHp(dmg);
                break;
        }
    }

    private void TrackBossHp(DamageEvent dmg)
    {
        if (_activeBossName is null) return;
        if (dmg.DestMaxHp is not { } max || max <= 0) return;
        if (dmg.DestCurrentHp is not { } cur || cur < 0) return;
        if (!string.Equals(dmg.DestName, _activeBossName, StringComparison.Ordinal)) return;
        _lastBossHp = (cur, max, dmg.Timestamp);
    }

    /// <summary>
    /// Final boss HP percent (0..100) at the moment of the last observed
    /// damage event against the encounter's named boss, or null if we never
    /// saw HP data — including multi-boss encounters whose ENCOUNTER_START
    /// name doesn't correspond to a single unit. Caller is responsible for
    /// filtering by content type + wipe.
    /// </summary>
    public double? GetLastBossHpPercent()
    {
        if (_lastBossHp is not { } hp) return null;
        if (hp.Max <= 0) return null;
        return Math.Clamp(hp.Current * 100.0 / hp.Max, 0.0, 100.0);
    }

    /// <summary>
    /// Snapshot of the per-GUID spec IDs captured from <c>COMBATANT_INFO</c>
    /// events so far. Exposed so <see cref="Build"/> can project these into
    /// the sidecar (and so tests can inspect capture without going through
    /// Build).
    /// </summary>
    public IReadOnlyDictionary<string, int> Combatants => _combatants;

    private void AddDamage(DamageEvent dmg)
    {
        if (string.IsNullOrEmpty(dmg.DestGuid)) return;
        EvictExpired(dmg.Timestamp);
        if (_damageCount >= MaxDamageEvents)
        {
            // Drop the oldest by walking until we find a non-empty bucket.
            // Cheap because LinkedList exposes First in O(1).
            foreach (var (_, list) in _damageByTarget)
            {
                if (list.First is not null)
                {
                    list.RemoveFirst();
                    _damageCount--;
                    break;
                }
            }
        }
        if (!_damageByTarget.TryGetValue(dmg.DestGuid, out var bucket))
        {
            bucket = new LinkedList<DamageEvent>();
            _damageByTarget[dmg.DestGuid] = bucket;
        }
        bucket.AddLast(dmg);
        _damageCount++;
    }

    private void EvictExpired(DateTimeOffset now)
    {
        var cutoff = now - _killingBlowLookback;
        foreach (var bucket in _damageByTarget.Values)
        {
            while (bucket.First is { } node && node.Value.Timestamp < cutoff)
            {
                bucket.RemoveFirst();
                _damageCount--;
            }
        }
    }

    public TimelineEventDocument Build(
        DateTimeOffset recordingEnd,
        double? preRollSeconds = null,
        double? postRollSeconds = null)
    {
        var events = new List<TimelineEvent>(_deaths.Count + _bossStarts.Count + _bossEnds.Count);

        foreach (var record in _deaths)
        {
            var death = record.Death;
            if (!IsPlayerGuid(death.DestGuid)) continue;
            events.Add(new PlayerDeathEvent(
                OffsetMs: ToOffsetMs(death.Timestamp),
                PlayerGuid: death.DestGuid,
                PlayerName: death.DestName ?? death.DestGuid,
                KilledBySource: record.Source,
                KilledBySpell: record.Spell,
                KillingBlowAmount: record.Amount));
        }

        foreach (var start in _bossStarts)
        {
            // Only emit boss starts that occur *strictly inside* the recording
            // window. The encounter that triggered the recording itself sits
            // at recordingStart and would be a duplicate of the clip header.
            if (start.Timestamp <= _recordingStart) continue;
            if (start.Timestamp >= recordingEnd) continue;
            events.Add(new BossStartEvent(
                OffsetMs: ToOffsetMs(start.Timestamp),
                EncounterId: start.EncounterId,
                EncounterName: start.EncounterName));
        }

        foreach (var end in _bossEnds)
        {
            // Symmetric to BossStart: skip the encounter end that *triggered*
            // recording stop (raid boss kill, or M+ key end) — that one sits
            // at recordingEnd and is already represented by the clip itself.
            if (end.Timestamp <= _recordingStart) continue;
            if (end.Timestamp >= recordingEnd) continue;
            events.Add(new BossEndEvent(
                OffsetMs: ToOffsetMs(end.Timestamp),
                EncounterId: end.EncounterId,
                EncounterName: end.EncounterName,
                Success: end.Success));
        }

        events.Sort((a, b) => a.OffsetMs.CompareTo(b.OffsetMs));

        var combatants = new List<CombatantRecord>(_combatants.Count);
        foreach (var (guid, specId) in _combatants)
        {
            // Specs we don't recognise (e.g. a newly-added expansion spec)
            // still yield a record so the data isn't lost; consumers render
            // them as "?". Class/Spec/Role come back null when SpecMap misses.
            var lookup = SpecMap.Lookup(specId);
            combatants.Add(new CombatantRecord(
                PlayerGuid: guid,
                SpecId: specId,
                Class: lookup?.Class,
                Spec: lookup?.Spec,
                Role: lookup?.Role));
        }

        return new TimelineEventDocument(
            Version: 2,
            RecordingStartedAt: _recordingStart,
            RecordingEndedAt: recordingEnd,
            Events: events)
        {
            Combatants = combatants,
            PreRollSeconds = preRollSeconds,
            PostRollSeconds = postRollSeconds,
        };
    }

    private (string? Source, string? Spell, long? Amount) FindKillingBlow(UnitDied death)
    {
        if (!_damageByTarget.TryGetValue(death.DestGuid, out var bucket) || bucket.Count == 0)
        {
            return (null, null, null);
        }
        var window = death.Timestamp - _killingBlowLookback;
        DamageEvent? best = null;
        foreach (var dmg in bucket)
        {
            if (dmg.Timestamp < window) continue;
            if (dmg.Timestamp > death.Timestamp) break;
            best = dmg; // last damage event in window wins (closest to death)
        }
        return best is null ? (null, null, null) : (best.SourceName, best.SpellName, best.Amount);
    }

    private int ToOffsetMs(DateTimeOffset when)
    {
        var ms = (when - _recordingStart).TotalMilliseconds;
        if (ms < 0) ms = 0;
        if (ms > int.MaxValue) ms = int.MaxValue;
        return (int)ms;
    }

    private static bool IsPlayerGuid(string? guid) =>
        !string.IsNullOrEmpty(guid) && guid.StartsWith("Player-", StringComparison.Ordinal);
}
