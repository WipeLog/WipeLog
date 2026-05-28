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

public class DamageEventParserTests
{
    private static readonly DateTimeOffset DefaultClockNow = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    private static CombatLogParser DefaultParser() => new(new FakeClock(DefaultClockNow));

    [Fact]
    public void Parses_each_damage_variant_from_fixture()
    {
        var lines = File.ReadAllLines(Path.Combine("Fixtures", "damage-events.txt"));
        var parser = DefaultParser();

        var events = lines.Select(l => parser.TryParseLine(l)).ToList();
        Assert.All(events, e => Assert.NotNull(e));

        var spell = Assert.IsType<DamageEvent>(events[0]);
        Assert.Equal("Creature-0-1469-2657-12-225829-0001", spell.SourceGuid);
        Assert.Equal("Imperator Averzian", spell.SourceName);
        Assert.Equal("Player-1-AELARA", spell.DestGuid);
        Assert.Equal("Aelara-DefiasBrotherhood-EU", spell.DestName);
        Assert.Equal(432123, spell.SpellId);
        Assert.Equal("Cleave", spell.SpellName);
        Assert.Equal(12345L, spell.Amount);

        var swing = Assert.IsType<DamageEvent>(events[1]);
        Assert.Null(swing.SpellId);
        Assert.Null(swing.SpellName);
        Assert.Equal(98765L, swing.Amount);

        var dot = Assert.IsType<DamageEvent>(events[2]);
        Assert.Equal("Decay", dot.SpellName);
        Assert.Equal(5500L, dot.Amount);

        var environmental = Assert.IsType<DamageEvent>(events[3]);
        Assert.Null(environmental.SourceGuid); // 0000000000000000 normalised to null
        Assert.Null(environmental.SourceName); // "nil" normalised to null
        Assert.Equal("Player-1-AELARA", environmental.DestGuid);
        Assert.Equal(99999L, environmental.Amount);
    }

    [Fact]
    public void Spell_damage_with_advanced_logging_block_still_picks_amount_from_suffix()
    {
        // Line includes 17 advanced-param fields between spell prefix and damage suffix.
        var parser = DefaultParser();
        var line =
            "5/10/2026 17:00:00.000  SPELL_DAMAGE," +
            "Creature-0-1-2-3-4-A,\"Boss\",0x10a48,0x0," +
            "Player-1-AELARA,\"Aelara\",0x511,0x0," +
            "12345,\"Smash\",1," +
            // 17 advanced params (infoGUID, ownerGUID, currentHP, maxHP, attackPower,
            // spellPower, armor, absorb, powerType, currentPower, maxPower, powerCost,
            // x, y, mapId, facing, level)
            "Player-1-AELARA,0000000000000000,500000,500000,1000,500,3000,0,3,100,100,0," +
            "1234.5,5678.9,2657,3.14,80," +
            // Damage suffix (10): amount, overkill, school, resisted, blocked, absorbed,
            // critical, glancing, crushing, isOffHand
            "77777,0,1,0,0,0,nil,nil,nil,nil";

        var evt = Assert.IsType<DamageEvent>(parser.TryParseLine(line));
        Assert.Equal(77777L, evt.Amount);
        Assert.Equal("Smash", evt.SpellName);
    }

    [Fact]
    public void Spell_damage_extracts_dest_hp_from_advanced_block()
    {
        // Boss (Creature-) hits a player; advanced block has the destination's
        // currentHP/maxHP after the player's GUID + ownerGUID.
        var parser = DefaultParser();
        var line =
            "5/10/2026 17:00:00.000  SPELL_DAMAGE," +
            "Creature-0-1-2-3-4-A,\"Boss\",0x10a48,0x0," +
            "Player-1-AELARA,\"Aelara\",0x511,0x0," +
            "12345,\"Smash\",1," +
            // 17 advanced params: [2]=currentHP=120000, [3]=maxHP=500000
            "Player-1-AELARA,0000000000000000,120000,500000,1000,500,3000,0,3,100,100,0," +
            "1234.5,5678.9,2657,3.14,80," +
            "77777,0,1,0,0,0,nil,nil,nil,nil";

        var evt = Assert.IsType<DamageEvent>(parser.TryParseLine(line));
        Assert.Equal(120000L, evt.DestCurrentHp);
        Assert.Equal(500000L, evt.DestMaxHp);
    }

    [Fact]
    public void Swing_damage_extracts_dest_hp_from_advanced_block()
    {
        var parser = DefaultParser();
        // SWING_DAMAGE has no spell prefix (3 fewer fields), so the advanced
        // block starts at index 9 instead of 12.
        var line =
            "5/10/2026 17:00:00.000  SWING_DAMAGE," +
            "Player-1-AELARA,\"Aelara\",0x511,0x0," +
            "Creature-0-1-2-3-4-A,\"Boss\",0x10a48,0x0," +
            // 17 advanced params: [2]=currentHP=8800000, [3]=maxHP=10000000
            "Creature-0-1-2-3-4-A,0000000000000000,8800000,10000000,1000,500,3000,0,3,100,100,0," +
            "1234.5,5678.9,2657,3.14,80," +
            // Damage suffix: amount, overkill, school, resisted, blocked, absorbed,
            // critical, glancing, crushing, isOffHand
            "55555,0,1,0,0,0,nil,nil,nil,nil";

        var evt = Assert.IsType<DamageEvent>(parser.TryParseLine(line));
        Assert.Equal("Boss", evt.DestName);
        Assert.Equal(8800000L, evt.DestCurrentHp);
        Assert.Equal(10000000L, evt.DestMaxHp);
    }

    [Fact]
    public void Damage_without_advanced_logging_leaves_dest_hp_null()
    {
        // No advanced block — fields between prefix and suffix have only the
        // 3 spell prefix fields, so the parser should leave HP null rather
        // than misread a damage-suffix value.
        var parser = DefaultParser();
        var line =
            "5/10/2026 17:00:00.000  SPELL_DAMAGE," +
            "Creature-0-1-2-3-4-A,\"Boss\",0x10a48,0x0," +
            "Player-1-AELARA,\"Aelara\",0x511,0x0," +
            "12345,\"Smash\",1," +
            "77777,0,1,0,0,0,nil,nil,nil,nil";

        var evt = Assert.IsType<DamageEvent>(parser.TryParseLine(line));
        Assert.Null(evt.DestCurrentHp);
        Assert.Null(evt.DestMaxHp);
    }

    [Fact]
    public void RealLog_PeriodicDamage_ParsedCorrectly()
    {
        // Real combat-log lines from a Chimaerus pull where Kheraval
        // died to Consuming Miasma. Pins the parser against the actual
        // modern-WoW damage field layout (advanced logging + trailing
        // type flag).
        var lines = File.ReadAllLines(Path.Combine("Fixtures", "killing-blow-real-log.txt"));
        var parser = DefaultParser();

        var events = lines
            .Select(l => parser.TryParseLine(l))
            .Where(e => e is not null)
            .Cast<CombatLogEvent>()
            .ToList();

        // 3 damage events + 1 UnitDied = 4 parsed events from 4 input lines.
        Assert.Equal(4, events.Count);
        var damageEvents = events.OfType<DamageEvent>().ToList();
        Assert.Equal(3, damageEvents.Count);

        var killingBlow = damageEvents.SingleOrDefault(d =>
            d.SpellName == "Consuming Miasma" &&
            d.SourceName == "Chimaerus" &&
            d.DestName == "Kheraval-DefiasBrotherhood-EU");
        Assert.NotNull(killingBlow);
        Assert.Equal("Player-1096-0A6F3B1E", killingBlow!.DestGuid);
    }

    [Fact]
    public void Damage_with_nil_destination_returns_null()
    {
        var parser = DefaultParser();
        var line = "5/10/2026 17:00:00.000  SPELL_DAMAGE," +
                   "Creature-0-1-2-3-4-A,\"Boss\",0x10a48,0x0," +
                   "0000000000000000,nil,0x80000000,0x80000000," +
                   "12345,\"Smash\",1," +
                   "1000,0,1,0,0,0,nil,nil,nil,nil";

        Assert.Null(parser.TryParseLine(line));
    }
}
