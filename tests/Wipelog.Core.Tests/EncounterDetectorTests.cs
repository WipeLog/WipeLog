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

using Microsoft.Extensions.Logging;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Detection;
using Xunit;

namespace Wipelog.Core.Tests;

public class EncounterDetectorTests
{
    private static DateTimeOffset T(int hour, int minute = 0, int second = 0) =>
        new(2026, 5, 9, hour, minute, second, TimeSpan.Zero);

    private static List<RecordingCommand> Run(EncounterDetector det, params CombatLogEvent[] events)
    {
        var output = new List<RecordingCommand>();
        foreach (var e in events)
        {
            output.AddRange(det.Process(e));
        }
        return output;
    }

    [Fact]
    public void Clean_raid_kill_emits_start_then_stop_with_kill_filename()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax the Devourer", 16, 20, 2657),
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax the Devourer", 16, 20, Success: true));

        Assert.Equal(2, commands.Count);
        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal("2026-05-09_19-43-01_ulgrax-the-devourer_mythic", start.SuggestedFilename);
        Assert.Equal(ContentType.RaidEncounter, start.Metadata.Type);
        Assert.Equal(2902, start.Metadata.EncounterId);

        var stop = Assert.IsType<StopRecording>(commands[1]);
        Assert.True(stop.Success);
        Assert.Equal("2026-05-09_19-43-01_ulgrax-the-devourer_mythic_kill.mp4", stop.SuggestedFilename);
    }

    [Fact]
    public void Raid_wipe_emits_stop_with_wipe_filename()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax the Devourer", 16, 20, 2657),
            new EncounterEnd(T(19, 45, 0), 2902, "Ulgrax the Devourer", 16, 20, Success: false));

        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.False(stop.Success);
        Assert.EndsWith("_wipe.mp4", stop.SuggestedFilename);
    }

    [Fact]
    public void Mythic_plus_timed_run_with_inner_bosses_emits_one_pair()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135, 136, 148 }),
            // Inner bosses inside M+ should NOT trigger new recordings.
            new EncounterStart(T(20, 18, 0), 2854, "E.D.N.A.", 8, 5, 2290),
            new EncounterEnd(T(20, 22, 0), 2854, "E.D.N.A.", 8, 5, Success: true),
            new EncounterStart(T(20, 28, 0), 2880, "Skarmorak", 8, 5, 2290),
            new EncounterEnd(T(20, 33, 0), 2880, "Skarmorak", 8, 5, Success: true),
            new ChallengeModeEnd(T(20, 43, 21), 2290, Success: true, KeystoneLevel: 12, Duration: TimeSpan.FromMilliseconds(1701500)));

        Assert.Equal(2, commands.Count);
        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal("2026-05-09_20-15-00_the-stonevault_+12", start.SuggestedFilename);
        Assert.Equal(ContentType.MythicPlus, start.Metadata.Type);
        Assert.Equal(12, start.Metadata.KeystoneLevel);

        var stop = Assert.IsType<StopRecording>(commands[1]);
        Assert.True(stop.Success);
        Assert.Equal("2026-05-09_20-15-00_the-stonevault_+12_timed.mp4", stop.SuggestedFilename);
    }

    [Fact]
    public void Mythic_plus_depleted_uses_depleted_suffix()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135, 136, 148 }),
            new ChallengeModeEnd(T(20, 50, 0), 2290, Success: false, KeystoneLevel: 12, Duration: TimeSpan.FromMinutes(35)));

        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.False(stop.Success);
        Assert.EndsWith("_depleted.mp4", stop.SuggestedFilename);
    }

    [Fact]
    public void Mythic_plus_records_challenge_mode_id_on_metadata()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135, 136, 148 }),
            new ChallengeModeEnd(T(20, 43, 0), 2290, Success: true, KeystoneLevel: 12, Duration: TimeSpan.FromMinutes(28)));

        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal(505, start.Metadata.ChallengeModeId);
    }

    [Fact]
    public void Mythic_plus_unknown_challenge_mode_id_leaves_upgrade_level_null()
    {
        // DungeonTimers ships empty — every M+ finishes with null upgrade
        // level until the table is populated. Detector must not invent
        // levels for unknown dungeons.
        var det = new EncounterDetector();
        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135, 136, 148 }),
            new ChallengeModeEnd(T(20, 43, 0), 2290, Success: true, KeystoneLevel: 12, Duration: TimeSpan.FromMinutes(28)));

        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.True(stop.Success);
        Assert.Null(stop.KeyUpgradeLevel);
    }

    [Fact]
    public void Mythic_plus_depleted_run_has_null_upgrade_level_even_when_par_known()
    {
        // Even if the dungeon's par time is known, a depleted run yields
        // null (not 0) — the detector only computes a level when Success
        // is true. 0/null distinction lives in the UI: depleted → "Depleted",
        // not "+0".
        var det = new EncounterDetector();
        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135, 136, 148 }),
            new ChallengeModeEnd(T(20, 50, 0), 2290, Success: false, KeystoneLevel: 12, Duration: TimeSpan.FromMinutes(35)));

        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.False(stop.Success);
        Assert.Null(stop.KeyUpgradeLevel);
    }

    [Fact]
    public void Zone_change_out_of_mythic_plus_stops_with_depleted_suffix()
    {
        // Abandoning a key (release → leave instance, hearth, disband) emits
        // no CHALLENGE_MODE_END — only a ZONE_CHANGE to wherever you land.
        // Reproduces the live bug seen on 2026-05-18 where Nexus-Point Xenas
        // (instanceId 2915) → Voidstorm (zoneId 2771) left the recording
        // running until daemon shutdown.
        var det = new EncounterDetector();
        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "Nexus-Point Xenas", 2915, 559, 16, new[] { 9, 10, 147 }),
            new ZoneChange(T(20, 17, 16), ZoneId: 2771, ZoneName: "Voidstorm", DifficultyId: 0));

        Assert.Equal(2, commands.Count);
        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.False(stop.Success);
        Assert.EndsWith("_depleted.mp4", stop.SuggestedFilename);
        Assert.Null(stop.KeyUpgradeLevel);
    }

    [Fact]
    public void Zone_change_to_same_dungeon_map_does_not_stop_mythic_plus()
    {
        // Defensive: a ZONE_CHANGE whose zoneId matches the active dungeon's
        // instanceId is a no-op (rare in practice — modern dungeons are one
        // map — but guard against it so we don't false-positive depletes).
        var det = new EncounterDetector();
        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135, 136, 148 }),
            new ZoneChange(T(20, 17, 0), ZoneId: 2290, ZoneName: "The Stonevault", DifficultyId: 8),
            new ChallengeModeEnd(T(20, 43, 0), 2290, Success: true, KeystoneLevel: 12, Duration: TimeSpan.FromMinutes(28)));

        Assert.Equal(2, commands.Count);
        Assert.IsType<StartRecording>(commands[0]);
        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.True(stop.Success);
        Assert.EndsWith("_timed.mp4", stop.SuggestedFilename);
    }

    [Fact]
    public void CombatLogVersion_mid_recording_emits_failure_stop_and_resets()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax the Devourer", 16, 20, 2657),
            new CombatLogVersion(T(19, 44, 0), Version: 22, AdvancedLogging: true));

        Assert.Equal(2, commands.Count);
        var stop = Assert.IsType<StopRecording>(commands[1]);
        Assert.False(stop.Success);

        // Detector should now be back in Idle and a new encounter starts cleanly.
        var more = Run(det,
            new EncounterStart(T(20, 0, 0), 2903, "Other", 16, 20, 2657));
        Assert.Single(more);
        Assert.IsType<StartRecording>(more[0]);
    }

    [Fact]
    public void Safety_timeout_fires_when_event_timestamp_exceeds_window()
    {
        var det = new EncounterDetector(new EncounterDetectorOptions
        {
            RaidTimeout = TimeSpan.FromMinutes(15),
        });
        var commands = Run(det,
            new EncounterStart(T(19, 0, 0), 2902, "Boss", 16, 20, 2657),
            // 16 minutes later — past the 15-minute raid timeout.
            new UnitDied(T(19, 16, 1), "Player-1-A", "Foo"));

        Assert.Equal(2, commands.Count);
        Assert.IsType<StartRecording>(commands[0]);
        var stop = Assert.IsType<StopRecording>(commands[1]);
        Assert.False(stop.Success);
        Assert.EndsWith("_wipe.mp4", stop.SuggestedFilename);
    }

    [Fact]
    public void Two_encounters_back_to_back_each_get_their_own_pair()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new EncounterStart(T(19, 0, 0), 1, "First", 16, 20, 2657),
            new EncounterEnd(T(19, 5, 0), 1, "First", 16, 20, Success: true),
            new EncounterStart(T(19, 6, 0), 2, "Second", 16, 20, 2657),
            new EncounterEnd(T(19, 12, 0), 2, "Second", 16, 20, Success: false));

        Assert.Equal(4, commands.Count);
        Assert.IsType<StartRecording>(commands[0]);
        var firstStop = Assert.IsType<StopRecording>(commands[1]);
        Assert.True(firstStop.Success);
        Assert.IsType<StartRecording>(commands[2]);
        var secondStop = Assert.IsType<StopRecording>(commands[3]);
        Assert.False(secondStop.Success);

        var first = (StartRecording)commands[0];
        var second = (StartRecording)commands[2];
        Assert.Equal(1, first.Metadata.EncounterId);
        Assert.Equal(2, second.Metadata.EncounterId);
    }

    [Fact]
    public void Mismatched_encounter_end_is_ignored()
    {
        // Edge case: ENCOUNTER_END with a different ID than the active encounter
        // should not stop the recording.
        var det = new EncounterDetector();
        var commands = Run(det,
            new EncounterStart(T(19, 0, 0), 100, "Main", 16, 20, 2657),
            new EncounterEnd(T(19, 1, 0), 999, "Other", 16, 20, Success: true));

        Assert.Single(commands);
        Assert.IsType<StartRecording>(commands[0]);
    }

    [Fact]
    public void Arena_match_emits_start_then_stop_with_bracket_and_complete_suffix()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ArenaMatchStart(T(20, 0, 0), InstanceId: 572, Bracket: "Arena3v3", MatchType: "Ranked"),
            new ArenaMatchEnd(T(20, 4, 30), WinningFaction: 1, Duration: TimeSpan.FromSeconds(270)));

        Assert.Equal(2, commands.Count);
        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal(ContentType.Arena, start.Metadata.Type);
        Assert.Equal("Arena3v3", start.Metadata.ArenaBracket);
        Assert.Equal("2026-05-09_20-00-00_arena3v3", start.SuggestedFilename);

        var stop = Assert.IsType<StopRecording>(commands[1]);
        Assert.True(stop.Success); // placeholder; RecorderController overrides at finalize
        Assert.EndsWith("_complete.mp4", stop.SuggestedFilename);
        // WinningFaction flows through unchanged from ArenaMatchEnd so the
        // controller can compare it against the player's faction.
        Assert.Equal(1, stop.WinningFaction);
    }

    [Theory]
    [InlineData(0)] // Horde wins
    [InlineData(1)] // Alliance wins
    public void Arena_StopRecording_carries_winning_faction_for_each_value(int faction)
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ArenaMatchStart(T(20, 0, 0), 572, "Arena3v3", "Ranked"),
            new ArenaMatchEnd(T(20, 5, 0), WinningFaction: faction, Duration: TimeSpan.FromSeconds(300)));

        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.Equal(faction, stop.WinningFaction);
    }

    [Fact]
    public void Non_arena_StopRecording_has_null_winning_faction()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new EncounterStart(T(19, 43, 1), 2902, "Ulgrax the Devourer", 16, 20, 2657),
            new EncounterEnd(T(19, 47, 55), 2902, "Ulgrax the Devourer", 16, 20, Success: true));

        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.Null(stop.WinningFaction);
    }

    [Fact]
    public void Solo_shuffle_bracket_classifies_as_SoloShuffle()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ArenaMatchStart(T(20, 0, 0), 572, Bracket: "Rated Solo Shuffle", MatchType: "Ranked"),
            new ArenaMatchEnd(T(20, 6, 0), 0, TimeSpan.FromSeconds(360)));

        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal(ContentType.SoloShuffle, start.Metadata.Type);
        Assert.Equal("Rated Solo Shuffle", start.Metadata.ArenaBracket);
        // Slug strips spaces; "rated-solo-shuffle" shows up in the filename.
        Assert.Contains("rated-solo-shuffle", start.SuggestedFilename);
    }

    [Fact]
    public void Skirmish_bracket_classifies_as_Arena_not_SoloShuffle()
    {
        // Defensive: "Skirmish" must not trip the Shuffle classifier.
        var det = new EncounterDetector();
        var commands = Run(det,
            new ArenaMatchStart(T(20, 0, 0), 572, Bracket: "Skirmish", MatchType: "Skirmish"));

        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal(ContentType.Arena, start.Metadata.Type);
    }

    [Fact]
    public void Arena_match_safety_timeout_respects_30_minute_window()
    {
        var det = new EncounterDetector();
        // Start an arena match, then receive an event 35 minutes later with no
        // ArenaMatchEnd in between. Default ArenaTimeout=30min → forced stop.
        var commands = Run(det,
            new ArenaMatchStart(T(20, 0, 0), 572, "Arena3v3", "Ranked"),
            new CombatantInfo(T(20, 35, 0), "Player-1-FROST", SpecId: 64));

        Assert.Equal(2, commands.Count);
        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.False(stop.Success); // timeout-induced stop is best-effort
    }

    [Fact]
    public void Arena_match_end_outside_recording_state_is_ignored()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ArenaMatchEnd(T(20, 0, 0), 1, TimeSpan.FromSeconds(180)));

        Assert.Empty(commands);
    }

    [Fact]
    public void Zone_change_into_BG_zone_starts_battleground_recording()
    {
        var det = new EncounterDetector();
        // Warsong Gulch (zoneId 489) is in the BG whitelist.
        var commands = Run(det,
            new ZoneChange(T(20, 0, 0), ZoneId: 489, ZoneName: "Warsong Gulch", DifficultyId: 0));

        var start = Assert.IsType<StartRecording>(Assert.Single(commands));
        Assert.Equal(ContentType.Battleground, start.Metadata.Type);
        Assert.Equal("Warsong Gulch", start.Metadata.DisplayName);
        Assert.Equal(489, start.Metadata.InstanceId);
        Assert.Equal("2026-05-09_20-00-00_warsong-gulch", start.SuggestedFilename);
    }

    [Fact]
    public void Zone_change_out_of_BG_stops_battleground_recording()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ZoneChange(T(20, 0, 0), 489, "Warsong Gulch", 0),
            new ZoneChange(T(20, 25, 0), ZoneId: 1, ZoneName: "Stormwind City", DifficultyId: 0));

        Assert.Equal(2, commands.Count);
        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.EndsWith("_complete.mp4", stop.SuggestedFilename);
        Assert.True(stop.Success);
    }

    [Fact]
    public void Zone_change_to_non_BG_outside_recording_state_does_nothing()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ZoneChange(T(20, 0, 0), ZoneId: 1, ZoneName: "Stormwind City", DifficultyId: 0));

        Assert.Empty(commands);
    }

    [Fact]
    public void Intra_BG_zone_change_with_same_zoneId_is_ignored()
    {
        // Belt-and-suspenders: a duplicated ZoneChange to the same zone (rare,
        // but defensively handled) should not stop the recording.
        var det = new EncounterDetector();
        var commands = Run(det,
            new ZoneChange(T(20, 0, 0), 489, "Warsong Gulch", 0),
            new ZoneChange(T(20, 5, 0), 489, "Warsong Gulch", 0));

        Assert.Single(commands);
        Assert.IsType<StartRecording>(commands[0]);
    }

    [Fact]
    public void Battleground_safety_timeout_respects_90_minute_window()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new ZoneChange(T(20, 0, 0), 489, "Warsong Gulch", 0),
            // 100 minutes later — past the default 90-minute timeout.
            new CombatantInfo(T(21, 40, 0), "Player-1-X", SpecId: 64));

        Assert.Equal(2, commands.Count);
        var stop = Assert.IsType<StopRecording>(commands[^1]);
        Assert.False(stop.Success); // timeout-induced stops are best-effort
    }

    [Fact]
    public void Arena_fixture_replays_into_one_start_one_stop_pair()
    {
        // Synthetic — real-log iteration may surface field-offset tweaks.
        var lines = File.ReadAllLines(Path.Combine("Fixtures", "arena-match.txt"));
        var parser = new CombatLogParser(new FakeClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
        var det = new EncounterDetector();

        var commands = new List<RecordingCommand>();
        foreach (var line in lines)
        {
            var evt = parser.TryParseLine(line);
            if (evt is not null) commands.AddRange(det.Process(evt));
        }

        Assert.Equal(2, commands.Count);
        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal(ContentType.Arena, start.Metadata.Type);
        Assert.Equal("Arena3v3", start.Metadata.ArenaBracket);

        var stop = Assert.IsType<StopRecording>(commands[1]);
        Assert.EndsWith("_complete.mp4", stop.SuggestedFilename);
    }

    [Fact]
    public void Battleground_fixture_replays_into_one_start_one_stop_pair()
    {
        var lines = File.ReadAllLines(Path.Combine("Fixtures", "battleground-zone.txt"));
        var parser = new CombatLogParser(new FakeClock(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero)));
        var det = new EncounterDetector();

        var commands = new List<RecordingCommand>();
        foreach (var line in lines)
        {
            var evt = parser.TryParseLine(line);
            if (evt is not null) commands.AddRange(det.Process(evt));
        }

        Assert.Equal(2, commands.Count);
        var start = Assert.IsType<StartRecording>(commands[0]);
        Assert.Equal(ContentType.Battleground, start.Metadata.Type);
        Assert.Equal("Warsong Gulch", start.Metadata.DisplayName);

        var stop = Assert.IsType<StopRecording>(commands[1]);
        Assert.EndsWith("_complete.mp4", stop.SuggestedFilename);
    }

    // ---- Recording filters --------------------------------------------------

    [Fact]
    public void Filtered_raid_difficulty_emits_no_start_and_stays_idle()
    {
        var filters = new RecordingFilters
        {
            RaidDifficulties = new[] { RaidDifficulty.Mythic },
        };
        var det = new EncounterDetector(filters: filters);

        // Heroic (15) is filtered out — no command, detector stays Idle.
        var heroic = Run(det,
            new EncounterStart(T(19, 0, 0), 2902, "Ulgrax the Devourer", 15, 20, 2657),
            new EncounterEnd(T(19, 5, 0), 2902, "Ulgrax the Devourer", 15, 20, Success: true));
        Assert.Empty(heroic);

        // Mythic (16) on the same detector still records — proves it stayed Idle.
        var mythic = Run(det,
            new EncounterStart(T(20, 0, 0), 2902, "Ulgrax the Devourer", 16, 20, 2657));
        var start = Assert.IsType<StartRecording>(Assert.Single(mythic));
        Assert.Equal(ContentType.RaidEncounter, start.Metadata.Type);
    }

    [Fact]
    public void Filtered_low_keystone_emits_no_start()
    {
        var filters = new RecordingFilters { MinKeystoneLevel = 10 };
        var det = new EncounterDetector(filters: filters);

        var commands = Run(det,
            new ChallengeModeStart(T(20, 15, 0), "The Stonevault", 2290, 505, 5, new[] { 9, 135 }));
        Assert.Empty(commands);
    }

    [Fact]
    public void UpdateFilters_applies_to_subsequent_starts()
    {
        // Start permissive: a +5 records.
        var det = new EncounterDetector(filters: new RecordingFilters { MinKeystoneLevel = 2 });
        var before = Run(det,
            new ChallengeModeStart(T(20, 0, 0), "The Stonevault", 2290, 505, 5, new[] { 9, 135 }),
            new ChallengeModeEnd(T(20, 30, 0), 2290, Success: true, KeystoneLevel: 5, Duration: TimeSpan.FromMinutes(30)));
        Assert.IsType<StartRecording>(before[0]);

        // Hot-swap the filters — now +5 is below the +10 minimum.
        det.UpdateFilters(new RecordingFilters { MinKeystoneLevel = 10 });
        Assert.Equal(10, det.CurrentFilters.MinKeystoneLevel);

        var after = Run(det,
            new ChallengeModeStart(T(21, 0, 0), "The Stonevault", 2290, 505, 5, new[] { 9, 135 }));
        Assert.Empty(after);

        // A +12 on the same (updated) detector still records.
        var high = Run(det,
            new ChallengeModeStart(T(22, 0, 0), "The Stonevault", 2290, 505, 12, new[] { 9, 135 }));
        Assert.IsType<StartRecording>(Assert.Single(high));
    }

    [Fact]
    public void RecordRaids_disabled_emits_no_start()
    {
        var det = new EncounterDetector(filters: new RecordingFilters { RecordRaids = false });
        var commands = Run(det,
            new EncounterStart(T(19, 0, 0), 2902, "Ulgrax the Devourer", 16, 20, 2657));
        Assert.Empty(commands);
    }

    [Fact]
    public void No_filters_records_everything_by_default()
    {
        var det = new EncounterDetector();
        var commands = Run(det,
            new EncounterStart(T(19, 0, 0), 2902, "Ulgrax the Devourer", 15, 20, 2657));
        Assert.IsType<StartRecording>(Assert.Single(commands));
    }

    [Fact]
    public void Filtered_skip_logs_at_information_with_actionable_message()
    {
        var logger = new CapturingLogger<EncounterDetector>();
        var filters = new RecordingFilters
        {
            RaidDifficulties = new[] { RaidDifficulty.Mythic },
        };
        var det = new EncounterDetector(filters: filters, logger: logger);

        Run(det, new EncounterStart(T(19, 0, 0), 2902, "Ulgrax the Devourer", 15, 20, 2657));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            "Skipping recording: raid Heroic Ulgrax the Devourer filtered (RaidDifficulties does not include Heroic)",
            entry.Message);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
