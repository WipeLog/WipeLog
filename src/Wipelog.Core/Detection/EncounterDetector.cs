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
using System.Text;
using Microsoft.Extensions.Logging;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Data;

namespace Wipelog.Core.Detection;

public sealed record EncounterDetectorOptions
{
    public TimeSpan RaidTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan MythicPlusTimeout { get; init; } = TimeSpan.FromMinutes(45);
    public TimeSpan ArenaTimeout { get; init; } = TimeSpan.FromMinutes(30);
    // Random BGs can run long with stalled scores — 90 minutes is the
    // conservative upper bound before we assume the log was abandoned.
    public TimeSpan BattlegroundTimeout { get; init; } = TimeSpan.FromMinutes(90);
}

/// <summary>
/// Pure state machine that turns a stream of <see cref="CombatLogEvent"/>
/// into <see cref="RecordingCommand"/>s. One event in, zero or more commands
/// out. No timers — all timeouts are evaluated against the timestamp of the
/// event being processed, which keeps the machine deterministic and trivially
/// testable.
/// </summary>
public sealed class EncounterDetector
{
    // Map IDs of the most common Battlegrounds. Best-guess starter set — to be
    // verified and expanded once real BG combat logs land in tests/Fixtures/.
    // TODO: confirm whether RBG runs differ from the unrated zone IDs; if they
    // share the same zoneId we'll need a secondary signal to detect "Rated".
    private static readonly HashSet<int> BattlegroundZoneIds = new()
    {
        30,    // Alterac Valley
        489,   // Warsong Gulch
        529,   // Arathi Basin
        566,   // Eye of the Storm
        607,   // Strand of the Ancients
        628,   // Isle of Conquest
        726,   // Twin Peaks
        761,   // Battle for Gilneas
    };

    private readonly EncounterDetectorOptions _options;
    private volatile RecordingFilters _filters;
    private readonly ILogger? _logger;

    private DetectorState _state = DetectorState.Idle;
    private RecordingMetadata? _active;
    private string? _activeStem;

    public EncounterDetector(
        EncounterDetectorOptions? options = null,
        RecordingFilters? filters = null,
        ILogger<EncounterDetector>? logger = null)
    {
        _options = options ?? new EncounterDetectorOptions();
        _filters = filters ?? new RecordingFilters();
        _logger = logger;
    }

    /// <summary>
    /// Hot-swap the recording filters at runtime. Safe to call from any thread:
    /// the filters are read only at the <see cref="DetectorState.Idle"/>→start
    /// transition (never mid-recording), and a <c>volatile</c> reference write
    /// is atomic, so an in-flight recording is unaffected and the next encounter
    /// is gated by the new rules. The daemon's preferences watcher calls this so
    /// GUI filter edits apply without a service restart.
    /// </summary>
    public void UpdateFilters(RecordingFilters filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        _filters = filters;
    }

    /// <summary>The filters currently in effect — test/diagnostic accessor.</summary>
    internal RecordingFilters CurrentFilters => _filters;

    public IEnumerable<RecordingCommand> Process(CombatLogEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (_state != DetectorState.Idle && _active is not null)
        {
            var timeout = _state switch
            {
                DetectorState.RecordingEncounter => _options.RaidTimeout,
                DetectorState.RecordingMythicPlus => _options.MythicPlusTimeout,
                DetectorState.RecordingArena => _options.ArenaTimeout,
                DetectorState.RecordingBattleground => _options.BattlegroundTimeout,
                _ => _options.RaidTimeout,
            };
            if (evt.Timestamp - _active.StartedAt > timeout)
            {
                yield return BuildStop(success: false, evt.Timestamp);
                Reset();
            }
        }

        if (evt is CombatLogVersion && _state != DetectorState.Idle)
        {
            yield return BuildStop(success: false, evt.Timestamp);
            Reset();
            yield break;
        }

        switch (_state)
        {
            case DetectorState.Idle:
                if (evt is EncounterStart es)
                {
                    if (_filters.AllowsRaid(es.DifficultyId, out var raidReason))
                    {
                        BeginRaid(es);
                        yield return BuildStart();
                    }
                    else
                    {
                        LogSkip($"raid {DifficultyMap.DisplayName(es.DifficultyId)} {es.EncounterName}", raidReason);
                    }
                }
                else if (evt is ChallengeModeStart cms)
                {
                    if (_filters.AllowsMythicPlus(cms.KeystoneLevel, out var mplusReason))
                    {
                        BeginMythicPlus(cms);
                        yield return BuildStart();
                    }
                    else
                    {
                        LogSkip($"Mythic+ +{cms.KeystoneLevel} {cms.ZoneName}", mplusReason);
                    }
                }
                else if (evt is ArenaMatchStart ams)
                {
                    BeginArena(ams);
                    yield return BuildStart();
                }
                else if (evt is ZoneChange zcStart && BattlegroundZoneIds.Contains(zcStart.ZoneId))
                {
                    if (_filters.AllowsBattleground(out var bgReason))
                    {
                        BeginBattleground(zcStart);
                        yield return BuildStart();
                    }
                    else
                    {
                        LogSkip($"battleground {zcStart.ZoneName}", bgReason);
                    }
                }
                break;

            case DetectorState.RecordingEncounter:
                if (evt is EncounterEnd ee && _active!.EncounterId == ee.EncounterId)
                {
                    yield return BuildStop(ee.Success, ee.Timestamp);
                    Reset();
                }
                break;

            case DetectorState.RecordingMythicPlus:
                if (evt is ChallengeModeEnd cme)
                {
                    // Use the game-authoritative duration on the event (more
                    // accurate than endedAt - startedAt for chest tiers) plus
                    // the dungeon's par time, when known.
                    int? upgradeLevel = null;
                    if (_active?.ChallengeModeId is { } cmId)
                    {
                        upgradeLevel = cme.Success
                            ? DungeonTimers.GetUpgradeLevel(cmId, cme.Duration)
                            : null;
                    }
                    yield return BuildStop(
                        cme.Success, cme.Timestamp, keyUpgradeLevel: upgradeLevel);
                    Reset();
                }
                else if (evt is ZoneChange zcMplus
                    && _active is not null
                    && zcMplus.ZoneId != _active.InstanceId)
                {
                    // Abandoned key: player released → leave instance, hearthed
                    // out, or the group disbanded. WoW does not emit
                    // CHALLENGE_MODE_END in any of those paths — the only clean
                    // signal is the ZONE_CHANGE to a map other than the
                    // dungeon's. The key drops a level regardless of why you
                    // left, so this is a depleted run (Success=false,
                    // KeyUpgradeLevel left null to match the explicit-deplete
                    // path). Multi-map dungeons (rare in modern WoW) would
                    // false-positive here — revisit if a real log surfaces one.
                    yield return BuildStop(success: false, zcMplus.Timestamp);
                    Reset();
                }
                // Inner ENCOUNTER_START / _END inside an M+ are intentionally ignored
                // here — they will be threaded into the timeline view in stage 5.
                break;

            case DetectorState.RecordingArena:
                if (evt is ArenaMatchEnd ame)
                {
                    // Win/loss is resolved at finalize time in RecorderController,
                    // where the recording player's faction is known. The detector
                    // surfaces WinningFaction so the controller can compare and
                    // overwrite Success + the filename suffix; placeholder values
                    // here keep the contract usable when faction isn't resolvable.
                    yield return BuildStop(success: true, ame.Timestamp,
                        winningFaction: ame.WinningFaction);
                    Reset();
                }
                break;

            case DetectorState.RecordingBattleground:
                // Any ZoneChange that leaves the BG zone stops the recording.
                // Intra-BG ZoneChange (same zoneId) is unusual but safe to
                // ignore; back-to-back BGs without an interim non-BG zone
                // would currently fold into one recording — flagged for a
                // later refinement once real BG logs verify the event flow.
                if (evt is ZoneChange zcEnd
                    && _active is not null
                    && zcEnd.ZoneId != _active.InstanceId)
                {
                    yield return BuildStop(success: true, zcEnd.Timestamp);
                    Reset();
                }
                break;
        }
    }

    private void BeginRaid(EncounterStart es)
    {
        _active = new RecordingMetadata(
            Type: ContentType.RaidEncounter,
            DisplayName: es.EncounterName,
            DifficultyId: es.DifficultyId,
            KeystoneLevel: null,
            EncounterId: es.EncounterId,
            InstanceId: es.InstanceId,
            StartedAt: es.Timestamp);
        _activeStem = BuildRaidStem(_active);
        _state = DetectorState.RecordingEncounter;
    }

    private void BeginMythicPlus(ChallengeModeStart cms)
    {
        _active = new RecordingMetadata(
            Type: ContentType.MythicPlus,
            DisplayName: cms.ZoneName,
            DifficultyId: null,
            KeystoneLevel: cms.KeystoneLevel,
            EncounterId: null,
            InstanceId: cms.InstanceId,
            StartedAt: cms.Timestamp,
            ChallengeModeId: cms.ChallengeModeId);
        _activeStem = BuildMythicPlusStem(_active);
        _state = DetectorState.RecordingMythicPlus;
    }

    private void BeginBattleground(ZoneChange zc)
    {
        // RBG vs unrated discrimination is deferred — the zoneId is the same
        // for both. Defaulting to ContentType.Battleground; the edit-metadata
        // dialog covers manual re-classification until a real RBG log shows
        // us a distinguishing signal.
        _active = new RecordingMetadata(
            Type: ContentType.Battleground,
            DisplayName: zc.ZoneName,
            DifficultyId: null,
            KeystoneLevel: null,
            EncounterId: null,
            InstanceId: zc.ZoneId,
            StartedAt: zc.Timestamp);
        _activeStem = BuildBattlegroundStem(_active);
        _state = DetectorState.RecordingBattleground;
    }

    private void BeginArena(ArenaMatchStart ams)
    {
        // Solo Shuffle uses the same ARENA_MATCH_START event as regular Arena;
        // the bracket string is what disambiguates ("Rated Solo Shuffle" /
        // "Arena3v3" / "Arena2v2" / "Skirmish" / ...). Anything with "shuffle"
        // in it lands as SoloShuffle so the rail's separate tab catches it.
        var isShuffle = ams.Bracket.Contains("Shuffle", StringComparison.OrdinalIgnoreCase);
        _active = new RecordingMetadata(
            Type: isShuffle ? ContentType.SoloShuffle : ContentType.Arena,
            DisplayName: ams.Bracket,
            DifficultyId: null,
            KeystoneLevel: null,
            EncounterId: null,
            InstanceId: ams.InstanceId,
            StartedAt: ams.Timestamp,
            ArenaBracket: ams.Bracket);
        _activeStem = BuildArenaStem(_active);
        _state = DetectorState.RecordingArena;
    }

    private StartRecording BuildStart() => new(_activeStem!, _active!);

    private StopRecording BuildStop(
        bool success,
        DateTimeOffset endedAt,
        int? winningFaction = null,
        int? keyUpgradeLevel = null)
    {
        string suffix = _active!.Type switch
        {
            ContentType.RaidEncounter => success ? "kill" : "wipe",
            ContentType.MythicPlus => success ? "timed" : "depleted",
            // PvP suffix is a placeholder. RecorderController rewrites
            // arena's _complete → _win / _loss at finalize time when both
            // WinningFaction (here) and player Faction (PlayerSpecResolver)
            // are known.
            ContentType.Arena or ContentType.SoloShuffle => "complete",
            ContentType.Battleground or ContentType.RatedBattleground => "complete",
            _ => success ? "ok" : "fail",
        };
        return new StopRecording(
            success, _active, $"{_activeStem}_{suffix}.mp4", endedAt,
            winningFaction, keyUpgradeLevel);
    }

    private void Reset()
    {
        _state = DetectorState.Idle;
        _active = null;
        _activeStem = null;
    }

    // Information level (not Warning): a filtered skip is expected behavior, but
    // visible enough that users can diagnose "why isn't this recording" without
    // turning on debug logging.
    private void LogSkip(string what, string reason) =>
        _logger?.LogInformation("Skipping recording: {What} filtered ({Reason})", what, reason);

    private static string BuildRaidStem(RecordingMetadata m) =>
        $"{FormatTimestamp(m.StartedAt)}_{Slugify(m.DisplayName)}_{DifficultyMap.Slug(m.DifficultyId)}";

    private static string BuildMythicPlusStem(RecordingMetadata m) =>
        $"{FormatTimestamp(m.StartedAt)}_{Slugify(m.DisplayName)}_+{m.KeystoneLevel?.ToString(CultureInfo.InvariantCulture) ?? "0"}";

    private static string BuildArenaStem(RecordingMetadata m) =>
        $"{FormatTimestamp(m.StartedAt)}_{Slugify(m.ArenaBracket ?? m.DisplayName)}";

    private static string BuildBattlegroundStem(RecordingMetadata m) =>
        $"{FormatTimestamp(m.StartedAt)}_{Slugify(m.DisplayName)}";

    private static string FormatTimestamp(DateTimeOffset ts) =>
        ts.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);

    private static string Slugify(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return "unknown";
        }
        var sb = new StringBuilder(input.Length);
        bool lastWasHyphen = true;
        foreach (var ch in input)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }
        if (sb.Length > 0 && sb[^1] == '-')
        {
            sb.Length--;
        }
        return sb.Length == 0 ? "unknown" : sb.ToString();
    }

    private enum DetectorState
    {
        Idle,
        RecordingEncounter,
        RecordingMythicPlus,
        RecordingArena,
        RecordingBattleground,
    }
}
