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
using Xunit;

namespace Wipelog.Core.Tests;

public class CombatLogParserTests
{
    private static readonly DateTimeOffset DefaultClockNow = new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    private static CombatLogParser ParserAt(DateTimeOffset now) => new(new FakeClock(now));
    private static CombatLogParser DefaultParser() => ParserAt(DefaultClockNow);

    [Fact]
    public void Parses_each_event_type_from_fixture()
    {
        var lines = File.ReadAllLines(Path.Combine("Fixtures", "sample-events.txt"));
        var parser = ParserAt(new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero));

        var events = lines.Select(l => parser.TryParseLine(l)).ToList();
        Assert.All(events, e => Assert.NotNull(e));

        var version = Assert.IsType<CombatLogVersion>(events[0]);
        Assert.Equal(22, version.Version);
        Assert.True(version.AdvancedLogging);

        var encStart = Assert.IsType<EncounterStart>(events[1]);
        Assert.Equal(2902, encStart.EncounterId);
        Assert.Equal("Ulgrax the Devourer", encStart.EncounterName);
        Assert.Equal(16, encStart.DifficultyId);
        Assert.Equal(20, encStart.GroupSize);
        Assert.Equal(2657, encStart.InstanceId);

        var encEnd = Assert.IsType<EncounterEnd>(events[2]);
        Assert.Equal(2902, encEnd.EncounterId);
        Assert.Equal("Ulgrax the Devourer", encEnd.EncounterName);
        Assert.Equal(16, encEnd.DifficultyId);
        Assert.Equal(20, encEnd.GroupSize);
        Assert.True(encEnd.Success);

        var cmStart = Assert.IsType<ChallengeModeStart>(events[3]);
        Assert.Equal("The Stonevault", cmStart.ZoneName);
        Assert.Equal(2290, cmStart.InstanceId);
        Assert.Equal(505, cmStart.ChallengeModeId);
        Assert.Equal(12, cmStart.KeystoneLevel);
        Assert.Equal(new[] { 9, 135, 136, 148 }, cmStart.Affixes);

        var cmEnd = Assert.IsType<ChallengeModeEnd>(events[4]);
        Assert.Equal(2290, cmEnd.InstanceId);
        Assert.True(cmEnd.Success);
        Assert.Equal(12, cmEnd.KeystoneLevel);
        Assert.Equal(TimeSpan.FromMilliseconds(1701500), cmEnd.Duration);

        var died = Assert.IsType<UnitDied>(events[5]);
        Assert.Equal("Player-1234-ABCDEF01", died.DestGuid);
        Assert.Equal("Yourname-Realm", died.DestName);
    }

    [Fact]
    public void Nil_fields_become_null()
    {
        var parser = DefaultParser();
        var line = "5/1 12:00:00.000  UNIT_DIED,0000000000000000,nil,0x80000000,0x80000000,Creature-0-0-0-0-1234-000000000A,nil,0x10a48,0x0";

        var evt = parser.TryParseLine(line);

        var died = Assert.IsType<UnitDied>(evt);
        Assert.Equal("Creature-0-0-0-0-1234-000000000A", died.DestGuid);
        Assert.Null(died.DestName);
    }

    [Fact]
    public void UnitDied_with_unconscious_flag_one_is_feign_death()
    {
        var parser = DefaultParser();
        var line = "5/12 21:43:33.667  UNIT_DIED,0000000000000000,nil,0x80000000,0x80000000,Player-1096-0870B20A,\"Orkantfany-DefiasBrotherhood-EU\",0x512,0x80000000,1";

        var died = Assert.IsType<UnitDied>(parser.TryParseLine(line));
        Assert.True(died.IsUnconscious);
    }

    [Fact]
    public void UnitDied_with_unconscious_flag_zero_is_real_death()
    {
        var parser = DefaultParser();
        var line = "5/12 21:10:28.867  UNIT_DIED,0000000000000000,nil,0x80000000,0x80000000,Player-1096-0870B20A,\"Orkantfany-DefiasBrotherhood-EU\",0x512,0x80000000,0";

        var died = Assert.IsType<UnitDied>(parser.TryParseLine(line));
        Assert.False(died.IsUnconscious);
    }

    [Fact]
    public void UnitDied_legacy_line_without_unconscious_flag_defaults_to_real_death()
    {
        var parser = DefaultParser();
        // Pre-Dragonflight log lines omit the trailing unconsciousOnDeath
        // field; parser must treat them as real deaths.
        var line = "5/1 12:00:00.000  UNIT_DIED,0000000000000000,nil,0x80000000,0x80000000,Player-1234-ABCDEF01,\"Yourname-Realm\",0x511,0x0";

        var died = Assert.IsType<UnitDied>(parser.TryParseLine(line));
        Assert.False(died.IsUnconscious);
    }

    [Fact]
    public void Quoted_strings_with_commas_are_not_split()
    {
        var parser = DefaultParser();
        var line = "5/1 12:00:00.000  ENCOUNTER_START,2902,\"Brood of Nashal, the Eternal\",16,20,2657";

        var evt = parser.TryParseLine(line);

        var encStart = Assert.IsType<EncounterStart>(evt);
        Assert.Equal("Brood of Nashal, the Eternal", encStart.EncounterName);
        Assert.Equal(2657, encStart.InstanceId);
    }

    [Fact]
    public void Bracketed_arrays_parse_to_int_array()
    {
        var parser = DefaultParser();
        var line = "5/1 12:00:00.000  CHALLENGE_MODE_START,\"The Stonevault\",2290,505,12,[9,135,136,148]";

        var evt = parser.TryParseLine(line);

        var cmStart = Assert.IsType<ChallengeModeStart>(evt);
        Assert.Equal(new[] { 9, 135, 136, 148 }, cmStart.Affixes);
    }

    [Fact]
    public void Year_rollover_uses_previous_year_when_month_in_future()
    {
        var parser = ParserAt(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
        var line = "12/20 19:00:00.000  ENCOUNTER_START,2902,\"Ulgrax the Devourer\",16,20,2657";

        var evt = parser.TryParseLine(line);

        Assert.NotNull(evt);
        Assert.Equal(2025, evt!.Timestamp.Year);
        Assert.Equal(12, evt.Timestamp.Month);
        Assert.Equal(20, evt.Timestamp.Day);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not a log line")]
    [InlineData("99/99 99:99:99.999  ENCOUNTER_START,1,\"x\",1,1,1")]
    [InlineData("11/9 19:42:13.451")]
    public void Malformed_line_returns_null(string line)
    {
        var parser = DefaultParser();

        var evt = parser.TryParseLine(line);

        Assert.Null(evt);
    }

    [Fact]
    public void Historical_offset_used_for_parsed_timestamp_not_clock_now()
    {
        // Clock is in May (PDT, -07:00) but the log entry is from November (PST, -08:00).
        // The parsed event must carry November's offset, not May's.
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var clockNow = new DateTimeOffset(2024, 5, 15, 12, 0, 0, TimeSpan.FromHours(-7));
        var parser = new CombatLogParser(new FakeClock(clockNow, pacific));
        var line = "11/15/2024 14:00:00.000  ENCOUNTER_START,2902,\"X\",16,20,2657";

        var evt = parser.TryParseLine(line);

        Assert.NotNull(evt);
        Assert.Equal(TimeSpan.FromHours(-8), evt!.Timestamp.Offset);
    }

    [Fact]
    public void Spring_forward_summer_event_parsed_in_winter_gets_dst_offset()
    {
        // Clock is in February (PST, -08:00) but log entry is from July (PDT, -07:00).
        // The parsed event must carry July's offset.
        var pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var clockNow = new DateTimeOffset(2024, 2, 15, 12, 0, 0, TimeSpan.FromHours(-8));
        var parser = new CombatLogParser(new FakeClock(clockNow, pacific));
        var line = "7/15/2024 14:00:00.000  ENCOUNTER_START,2902,\"X\",16,20,2657";

        var evt = parser.TryParseLine(line);

        Assert.NotNull(evt);
        Assert.Equal(TimeSpan.FromHours(-7), evt!.Timestamp.Offset);
    }

    [Fact]
    public void Modern_timestamp_with_year_and_4_digit_milliseconds_parses()
    {
        var parser = DefaultParser();
        var line = "5/9/2026 11:01:28.9902  ENCOUNTER_START,3212,\"Muro'jin and Nekraxx\",8,5,2874";

        var evt = parser.TryParseLine(line);

        var es = Assert.IsType<EncounterStart>(evt);
        Assert.Equal(2026, es.Timestamp.Year);
        Assert.Equal(5, es.Timestamp.Month);
        Assert.Equal(9, es.Timestamp.Day);
        Assert.Equal(11, es.Timestamp.Hour);
        Assert.Equal(28, es.Timestamp.Second);
        Assert.Equal(990, es.Timestamp.Millisecond);
        Assert.Equal(3212, es.EncounterId);
        Assert.Equal("Muro'jin and Nekraxx", es.EncounterName);
    }

    [Fact]
    public void Modern_encounter_end_with_extra_duration_field_still_parses()
    {
        // Modern WoW retail appends an encounter-duration field (millis) after success.
        var parser = DefaultParser();
        var line = "5/9/2026 11:03:40.5042  ENCOUNTER_END,3212,\"Muro'jin and Nekraxx\",8,5,1,131547";

        var evt = parser.TryParseLine(line);

        var ee = Assert.IsType<EncounterEnd>(evt);
        Assert.True(ee.Success);
        Assert.Equal(3212, ee.EncounterId);
    }

    [Fact]
    public void Modern_challenge_mode_end_with_extra_score_fields_still_parses()
    {
        var parser = DefaultParser();
        var line = "5/9/2026 11:16:12.6992  CHALLENGE_MODE_END,2874,1,10,1305624,332.772278,2281.906738";

        var evt = parser.TryParseLine(line);

        var cme = Assert.IsType<ChallengeModeEnd>(evt);
        Assert.True(cme.Success);
        Assert.Equal(10, cme.KeystoneLevel);
        Assert.Equal(TimeSpan.FromMilliseconds(1305624), cme.Duration);
    }

    [Fact]
    public void Unknown_event_produces_UnknownEvent_with_raw_fields()
    {
        var parser = DefaultParser();
        var line = "11/9 19:50:00.000  SOME_NEW_EVENT,foo,bar,42";

        var evt = parser.TryParseLine(line);

        var unknown = Assert.IsType<UnknownEvent>(evt);
        Assert.Equal("SOME_NEW_EVENT", unknown.EventName);
        Assert.Equal(new[] { "foo", "bar", "42" }, unknown.Fields);
    }

    [Fact]
    public void Arena_match_start_parses_bracket_and_match_type()
    {
        var parser = DefaultParser();
        var line = "5/1 12:00:00.000  ARENA_MATCH_START,572,30,Arena3v3,Ranked";

        var evt = parser.TryParseLine(line);

        var ams = Assert.IsType<ArenaMatchStart>(evt);
        Assert.Equal(572, ams.InstanceId);
        Assert.Equal("Arena3v3", ams.Bracket);
        Assert.Equal("Ranked", ams.MatchType);
    }

    [Fact]
    public void Arena_match_start_with_solo_shuffle_bracket_round_trips()
    {
        var parser = DefaultParser();
        // The encounter detector uses this string to classify Solo Shuffle; the
        // parser just round-trips it verbatim.
        var line = "5/1 12:00:00.000  ARENA_MATCH_START,572,6,Rated Solo Shuffle,Ranked";

        var evt = parser.TryParseLine(line);

        var ams = Assert.IsType<ArenaMatchStart>(evt);
        Assert.Equal("Rated Solo Shuffle", ams.Bracket);
    }

    [Fact]
    public void Arena_match_end_carries_winning_faction_and_duration_seconds()
    {
        var parser = DefaultParser();
        // matchDuration is in seconds in modern logs.
        var line = "5/1 12:25:00.000  ARENA_MATCH_END,1,1500,1480,1520";

        var evt = parser.TryParseLine(line);

        var ame = Assert.IsType<ArenaMatchEnd>(evt);
        Assert.Equal(1, ame.WinningFaction);
        Assert.Equal(TimeSpan.FromSeconds(1500), ame.Duration);
    }

    [Fact]
    public void Zone_change_parses_id_name_and_difficulty()
    {
        var parser = DefaultParser();
        var line = "5/1 12:00:00.000  ZONE_CHANGE,572,Ruins of Lordaeron,0";

        var evt = parser.TryParseLine(line);

        var zc = Assert.IsType<ZoneChange>(evt);
        Assert.Equal(572, zc.ZoneId);
        Assert.Equal("Ruins of Lordaeron", zc.ZoneName);
        Assert.Equal(0, zc.DifficultyId);
    }

    [Fact]
    public void Zone_change_with_quoted_zone_name_containing_comma_stays_one_field()
    {
        var parser = DefaultParser();
        var line = "5/1 12:00:00.000  ZONE_CHANGE,489,\"Warsong Gulch, the Old Way\",0";

        var evt = parser.TryParseLine(line);

        var zc = Assert.IsType<ZoneChange>(evt);
        Assert.Equal(489, zc.ZoneId);
        Assert.Equal("Warsong Gulch, the Old Way", zc.ZoneName);
    }

    [Fact]
    public void Truncated_arena_match_start_returns_null()
    {
        var parser = DefaultParser();
        // Missing bracket and matchType — older or partial logs.
        var line = "5/1 12:00:00.000  ARENA_MATCH_START,572,30";

        var evt = parser.TryParseLine(line);

        Assert.Null(evt);
    }
}
