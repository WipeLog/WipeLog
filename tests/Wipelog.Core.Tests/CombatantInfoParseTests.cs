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
using Xunit;

namespace Wipelog.Core.Tests;

public class CombatantInfoParseTests
{
    private static CombatLogParser DefaultParser() =>
        new(new FakeClock(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Parses_real_fixture_with_known_spec()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "combatant-info-sample.txt");
        var line = File.ReadAllText(path).TrimEnd();

        var evt = DefaultParser().TryParseLine(line);

        var ci = Assert.IsType<CombatantInfo>(evt);
        Assert.Equal("Player-1096-0AF906FB", ci.PlayerGuid);
        Assert.Equal(64, ci.SpecId); // Frost Mage in this fixture
        Assert.True(SpecMap.Known.ContainsKey(ci.SpecId));
        // Field [2] in the fixture is "0" — Horde.
        Assert.Equal(0, ci.Faction);
    }

    [Theory]
    [InlineData("0", 0)]   // Horde
    [InlineData("1", 1)]   // Alliance
    public void Parses_faction_from_field_2(string raw, int expected)
    {
        var fields = new List<string> { "Player-1-A", raw };
        for (int i = 0; i < 25; i++) fields.Add("999");
        fields.Add("64"); // Frost Mage, in scan range
        var line = "5/9/2026 19:00:00.000  COMBATANT_INFO," + string.Join(",", fields);

        var evt = DefaultParser().TryParseLine(line);

        var ci = Assert.IsType<CombatantInfo>(evt);
        Assert.Equal(expected, ci.Faction);
    }

    [Fact]
    public void Malformed_faction_field_yields_null_but_keeps_spec()
    {
        // Faction value that isn't an int — should null out faction but
        // not block the rest of the record.
        var fields = new List<string> { "Player-1-A", "not-an-int" };
        for (int i = 0; i < 25; i++) fields.Add("999");
        fields.Add("64");
        var line = "5/9/2026 19:00:00.000  COMBATANT_INFO," + string.Join(",", fields);

        var evt = DefaultParser().TryParseLine(line);

        var ci = Assert.IsType<CombatantInfo>(evt);
        Assert.Null(ci.Faction);
        Assert.Equal(64, ci.SpecId);
    }

    [Fact]
    public void Returns_null_when_no_known_spec_id_in_candidate_range()
    {
        // Synthetic line with all stat fields set to non-spec ints.
        var stats = string.Join(",", Enumerable.Repeat("999", 30));
        var line = $"5/9/2026 19:00:00.000  COMBATANT_INFO,Player-1-A,0,{stats}";

        var evt = DefaultParser().TryParseLine(line);

        Assert.Null(evt);
    }

    [Fact]
    public void Falls_back_through_candidate_range_until_known_spec_hits()
    {
        // Place a known spec at index 27 (within scan range).
        var prefix = string.Join(",", Enumerable.Repeat("999", 25));
        // Body fields after event name: GUID + 999s + spec at index 27 (scan range 23..30).
        var line = $"5/9/2026 19:00:00.000  COMBATANT_INFO,Player-1-A,{prefix},266,[]";
        // Field layout in body: [0]=COMBATANT_INFO [1]=GUID [2]=0 [3..27]=999*25 [28]=266
        // Actually we need spec to land at one of indices 23-30. Build precisely.
        var fields = new List<string> { "Player-1-A" };
        for (int i = 0; i < 26; i++) fields.Add("999");  // pads up to body index 27
        fields.Add("266"); // index 28 — Demonology Warlock, in scan range
        line = "5/9/2026 19:00:00.000  COMBATANT_INFO," + string.Join(",", fields);

        var evt = DefaultParser().TryParseLine(line);

        var ci = Assert.IsType<CombatantInfo>(evt);
        Assert.Equal(266, ci.SpecId);
    }
}
