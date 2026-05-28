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
using Wipelog.Core.Detection;
using Xunit;

namespace Wipelog.Core.Tests;

public class PlayerSpecResolverTests
{
    // Lowest 4 bits of sourceFlags encode COMBATLOG_OBJECT_AFFILIATION_*:
    // 0x1 = MINE (recording player), 0x4 = RAID. Upper bits are the same
    // (0x500 = TYPE_PLAYER + CONTROL_PLAYER + REACTION_FRIENDLY).
    private const string MineFlags = "0x511";
    private const string RaidFlags = "0x514";

    private static DateTimeOffset T(int s) => new(2026, 5, 9, 19, 0, s, TimeSpan.Zero);

    private static UnknownEvent PrefixedEvent(
        string sourceGuid,
        string sourceName,
        string sourceFlags = RaidFlags)
    {
        // Standard combat-log prefix layout in UnknownEvent.Fields after the event name:
        // [0]=sourceGuid, [1]=sourceName, [2]=sourceFlags, [3]=sourceRaidFlags,
        // [4]=destGuid, [5]=destName, [6]=destFlags, [7]=destRaidFlags
        // Default flags are RAID-member (no MINE) — pass MineFlags explicitly
        // when this event represents the recording player.
        return new UnknownEvent(T(0), "SPELL_CAST_SUCCESS",
            new[] { sourceGuid, sourceName, sourceFlags, "0x0", "00000000", "nil", "0x0", "0x0" });
    }

    private static UnknownEvent PrefixedEventAt(
        int second,
        string sourceGuid,
        string sourceName,
        string sourceFlags = RaidFlags)
    {
        return new UnknownEvent(T(second), "SPELL_CAST_SUCCESS",
            new[] { sourceGuid, sourceName, sourceFlags, "0x0", "00000000", "nil", "0x0", "0x0" });
    }

    [Fact]
    public void Resolves_to_class_and_spec_when_buffer_has_prefixed_event_and_combatant_info()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", MineFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", 64), // Frost Mage
        };

        var id = new PlayerSpecResolver().Resolve(buffer, "Frostytanegl");

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
        Assert.Equal("Frostytanegl", id.Name);
        Assert.Equal("Mage", id.Class);
        Assert.Equal("Frost", id.Spec);
    }

    [Fact]
    public void Diacritic_match_resolves_when_hint_lacks_accent()
    {
        // No MINE flag — forces the explicit-hint path so the fold logic
        // is what actually runs.
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytånegl", RaidFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", 64),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, "Frostytanegl");

        Assert.NotNull(id);
        Assert.Equal("Frostytånegl", id!.Name); // unicode form preserved for storage
        Assert.Equal("Mage", id.Class);
    }

    [Fact]
    public void Auto_detects_top_player_guid_when_player_name_null()
    {
        // Frequency path: no MINE, no hint. Most-frequent Player-* wins.
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 50; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytånegl", RaidFlags));
        for (int i = 0; i < 5; i++)
            buffer.Add(PrefixedEvent("Player-1-OTHER", "Someoneelse", RaidFlags));
        for (int i = 0; i < 100; i++)
            buffer.Add(PrefixedEvent("Creature-0-1234-5678-9", "Bigboss"));
        buffer.Add(new CombatantInfo(T(1), "Player-1-FROSTY", 64));

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
        Assert.Equal("Frostytånegl", id.Name);
        Assert.Equal("Mage", id.Class);
        Assert.Equal("Frost", id.Spec);
    }

    [Fact]
    public void Auto_detect_runs_when_explicit_hint_finds_no_match()
    {
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 30; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytånegl", RaidFlags));
        buffer.Add(new CombatantInfo(T(1), "Player-1-FROSTY", 64));

        var id = new PlayerSpecResolver().Resolve(buffer, "WrongName");

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
        Assert.Equal("Frostytånegl", id.Name);
        Assert.Equal("Frost", id.Spec);
    }

    [Fact]
    public void NPC_guids_are_ignored_in_auto_detect()
    {
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 1000; i++)
            buffer.Add(PrefixedEvent("Creature-0-1234-5678-9", "Bigboss"));
        for (int i = 0; i < 10; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytanegl", RaidFlags));
        buffer.Add(new CombatantInfo(T(1), "Player-1-FROSTY", 64));

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
    }

    [Fact]
    public void Pet_and_vehicle_guids_are_ignored_in_auto_detect()
    {
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 100; i++)
            buffer.Add(PrefixedEvent("Pet-0-1234", "FluffyPet"));
        for (int i = 0; i < 100; i++)
            buffer.Add(PrefixedEvent("Vehicle-0-1234", "Mount"));
        for (int i = 0; i < 5; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytanegl", RaidFlags));
        buffer.Add(new CombatantInfo(T(1), "Player-1-FROSTY", 64));

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
    }

    [Fact]
    public void Returns_partial_identity_when_no_combatant_info_for_top_guid()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", MineFlags),
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", MineFlags),
            // No CombatantInfo for that GUID.
        };

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
        Assert.Equal("Frostytanegl", id.Name);
        Assert.Null(id.Class);
        Assert.Null(id.Spec);
    }

    [Fact]
    public void Returns_null_when_buffer_is_empty()
    {
        Assert.Null(new PlayerSpecResolver().Resolve(new List<CombatLogEvent>(), playerName: null));
        Assert.Null(new PlayerSpecResolver().Resolve(new List<CombatLogEvent>(), "Frostytanegl"));
    }

    [Fact]
    public void Returns_null_when_buffer_has_no_player_sources()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Creature-0-1234", "Bigboss"),
            PrefixedEvent("Pet-0-9999", "Fluffy"),
        };

        Assert.Null(new PlayerSpecResolver().Resolve(buffer, playerName: null));
    }

    [Fact]
    public void Empty_player_name_falls_through_to_auto_detect()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", RaidFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", 64),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, "");

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
        Assert.Equal("Frost", id.Spec);
    }

    // --- AFFILIATION_MINE tests -------------------------------------------------

    [Fact]
    public void Mine_flag_wins_over_higher_event_count_from_other_player()
    {
        // The literal regression case: in a raid, the tank's source events
        // outnumber the recording player's, but only the recording player
        // has the MINE bit set.
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 50; i++)
            buffer.Add(PrefixedEvent("Player-2-TANK", "Protwarrior", RaidFlags));
        for (int i = 0; i < 5; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytånegl", MineFlags));
        buffer.Add(new CombatantInfo(T(1), "Player-1-FROSTY", 64));
        buffer.Add(new CombatantInfo(T(1), "Player-2-TANK", 73));

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
        Assert.Equal("Frostytånegl", id.Name);
        Assert.Equal("Mage", id.Class);
        Assert.Equal("Frost", id.Spec);
    }

    [Fact]
    public void Mine_flag_wins_over_explicit_hint_pointing_at_someone_else()
    {
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 3; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytånegl", MineFlags));
        for (int i = 0; i < 10; i++)
            buffer.Add(PrefixedEvent("Player-2-TANK", "Protwarrior", RaidFlags));
        buffer.Add(new CombatantInfo(T(1), "Player-1-FROSTY", 64));
        buffer.Add(new CombatantInfo(T(1), "Player-2-TANK", 73));

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: "Protwarrior");

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid); // MINE beats explicit hint
    }

    [Fact]
    public void Falls_back_to_explicit_hint_when_no_mine_flag_present()
    {
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 20; i++)
            buffer.Add(PrefixedEvent("Player-2-TANK", "Protwarrior", RaidFlags));
        for (int i = 0; i < 3; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytånegl", RaidFlags));
        buffer.Add(new CombatantInfo(T(1), "Player-1-FROSTY", 64));

        var id = new PlayerSpecResolver().Resolve(buffer, "Frostytånegl");

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
    }

    [Fact]
    public void Falls_back_to_frequency_when_no_mine_and_no_hint()
    {
        var buffer = new List<CombatLogEvent>();
        for (int i = 0; i < 50; i++)
            buffer.Add(PrefixedEvent("Player-2-TANK", "Protwarrior", RaidFlags));
        for (int i = 0; i < 5; i++)
            buffer.Add(PrefixedEvent("Player-1-FROSTY", "Frostytånegl", RaidFlags));
        buffer.Add(new CombatantInfo(T(1), "Player-2-TANK", 73));

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-2-TANK", id!.PlayerGuid); // frequency picks the wrong player by design
    }

    [Fact]
    public void Malformed_source_flags_treated_as_no_mine()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", "garbage"),
            PrefixedEvent("Player-2-TANK", "Protwarrior", RaidFlags),
            PrefixedEvent("Player-2-TANK", "Protwarrior", RaidFlags),
            new CombatantInfo(T(1), "Player-2-TANK", 73),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        // Garbage flags don't crash and don't trigger MINE — frequency picks TANK.
        Assert.NotNull(id);
        Assert.Equal("Player-2-TANK", id!.PlayerGuid);
    }

    [Fact]
    public void Mine_flag_with_no_combatant_info_returns_partial_identity()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytånegl", MineFlags),
            PrefixedEvent("Player-1-FROSTY", "Frostytånegl", MineFlags),
            // No CombatantInfo at all.
        };

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
        Assert.Equal("Frostytånegl", id.Name);
        Assert.Null(id.Class);
        Assert.Null(id.Spec);
    }

    [Fact]
    public void Mine_flag_on_npc_guid_is_ignored()
    {
        // Defensive: the MINE scan reuses IsPlayerGuid so a Creature-* with
        // 0x511 (impossible in real logs) shouldn't be picked.
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Creature-0-1234", "Bigboss", MineFlags),
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", RaidFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", 64),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid);
    }

    [Fact]
    public void Empty_source_flags_field_treated_as_no_mine()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", ""),
            PrefixedEvent("Player-2-TANK", "Protwarrior", RaidFlags),
            new CombatantInfo(T(1), "Player-2-TANK", 73),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        // Frequency path runs because no MINE was found; tied 1-1 — order
        // by descending count then dictionary order picks one of them.
        // The important behaviour: no crash and a Player-* wins.
        Assert.NotNull(id);
        Assert.StartsWith("Player-", id!.PlayerGuid);
    }

    [Theory]
    [InlineData("Aelara-DefiasBrotherhood-EU", "Aelara", "DefiasBrotherhood", "EU")]
    [InlineData("Frostytånegl-Stormscale-EU", "Frostytånegl", "Stormscale", "EU")]
    [InlineData("Frostytanegl-Stormscale", "Frostytanegl", "Stormscale", null)]
    [InlineData("Frostytanegl", "Frostytanegl", null, null)]
    [InlineData("", "", null, null)]
    public void ParseFullName_splits_name_realm_region(
        string input, string expectedName, string? expectedRealm, string? expectedRegion)
    {
        var (name, realm, region) = PlayerIdentity.ParseFullName(input);
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedRealm, realm);
        Assert.Equal(expectedRegion, region);
    }

    [Fact]
    public void Resolved_identity_carries_split_realm_and_region()
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-AELARA", "Aelara-DefiasBrotherhood-EU", MineFlags),
            new CombatantInfo(T(1), "Player-1-AELARA", 70), // Ret Pal
        };

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Aelara", id!.Name);
        Assert.Equal("DefiasBrotherhood", id.Realm);
        Assert.Equal("EU", id.Region);
        Assert.Equal("Paladin", id.Class);
        Assert.Equal("Retribution", id.Spec);
    }

    [Fact]
    public void Bare_name_hint_matches_fully_qualified_source_name_via_fold()
    {
        // Hint is just "Frostytanegl"; source is the cross-realm form.
        // Without MINE in the buffer, the explicit-hint path runs and must
        // match the bare name part before the first dash.
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytånegl-Stormscale-EU", RaidFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", 64),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, "Frostytanegl");

        Assert.NotNull(id);
        Assert.Equal("Frostytånegl", id!.Name);
        Assert.Equal("Stormscale", id.Realm);
        Assert.Equal("EU", id.Region);
    }

    [Fact]
    public void Conflicting_mine_flags_pick_most_frequent_then_most_recent()
    {
        // Synthetic: two GUIDs both have MINE. Real logs shouldn't produce
        // this, but pin the tiebreak so the warning path is deterministic.
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEventAt(0, "Player-1-FROSTY", "Frostytanegl", MineFlags),
            PrefixedEventAt(1, "Player-1-FROSTY", "Frostytanegl", MineFlags),
            PrefixedEventAt(2, "Player-2-OTHER", "SomeOther", MineFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", 64),
            new CombatantInfo(T(1), "Player-2-OTHER", 73),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, playerName: null);

        Assert.NotNull(id);
        Assert.Equal("Player-1-FROSTY", id!.PlayerGuid); // 2 events vs 1
    }

    [Theory]
    [InlineData(0)]  // Horde
    [InlineData(1)]  // Alliance
    public void Faction_round_trips_from_CombatantInfo_to_identity(int faction)
    {
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", MineFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", SpecId: 64, Faction: faction),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, "Frostytanegl");

        Assert.NotNull(id);
        Assert.Equal(faction, id!.Faction);
    }

    [Fact]
    public void Null_faction_on_CombatantInfo_yields_null_identity_faction()
    {
        // Logs from older patches or with malformed faction field — resolver
        // still produces a usable identity, just without faction info.
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", MineFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", SpecId: 64, Faction: null),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, "Frostytanegl");

        Assert.NotNull(id);
        Assert.Null(id!.Faction);
        Assert.Equal("Mage", id.Class); // still resolves spec
    }

    [Fact]
    public void Faction_survives_unrecognised_spec_id()
    {
        // PvP win/loss needs faction even when SpecMap doesn't know the spec
        // (e.g. a freshly added expansion spec). Class/Spec come back null,
        // Faction does not.
        var buffer = new List<CombatLogEvent>
        {
            PrefixedEvent("Player-1-FROSTY", "Frostytanegl", MineFlags),
            new CombatantInfo(T(1), "Player-1-FROSTY", SpecId: 99999, Faction: 1),
        };

        var id = new PlayerSpecResolver().Resolve(buffer, "Frostytanegl");

        Assert.NotNull(id);
        Assert.Null(id!.Class);
        Assert.Null(id.Spec);
        Assert.Equal(1, id.Faction);
    }
}
