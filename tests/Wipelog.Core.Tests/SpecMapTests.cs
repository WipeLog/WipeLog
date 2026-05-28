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

using Wipelog.Core.Data;
using Xunit;

namespace Wipelog.Core.Tests;

public class SpecMapTests
{
    [Fact]
    public void Covers_all_thirteen_classes()
    {
        var classes = SpecMap.Known.Values.Select(v => v.Class).Distinct().ToHashSet();
        // 13 retail classes as of TWW.
        Assert.Equal(13, classes.Count);
    }

    [Theory]
    [InlineData(64, "Mage", "Frost")]
    [InlineData(266, "Warlock", "Demonology")]
    [InlineData(71, "Warrior", "Arms")]
    [InlineData(105, "Druid", "Restoration")]
    [InlineData(577, "Demon Hunter", "Havoc")]
    [InlineData(1480, "Demon Hunter", "Devourer")]
    [InlineData(1473, "Evoker", "Augmentation")]
    public void Spot_check_known_specs(int id, string expectedClass, string expectedSpec)
    {
        var v = SpecMap.Lookup(id);
        Assert.NotNull(v);
        Assert.Equal(expectedClass, v!.Value.Class);
        Assert.Equal(expectedSpec, v.Value.Spec);
    }

    [Fact]
    public void Unknown_spec_id_returns_null()
    {
        Assert.Null(SpecMap.Lookup(99999));
    }

    [Theory]
    [InlineData(250, SpecRole.Tank)]    // Blood DK
    [InlineData(581, SpecRole.Tank)]    // Vengeance DH
    [InlineData(104, SpecRole.Tank)]    // Guardian Druid
    [InlineData(268, SpecRole.Tank)]    // Brewmaster Monk
    [InlineData(66,  SpecRole.Tank)]    // Protection Paladin
    [InlineData(73,  SpecRole.Tank)]    // Protection Warrior
    [InlineData(105, SpecRole.Healer)]  // Restoration Druid
    [InlineData(1468, SpecRole.Healer)] // Preservation Evoker
    [InlineData(270, SpecRole.Healer)]  // Mistweaver Monk
    [InlineData(65,  SpecRole.Healer)]  // Holy Paladin
    [InlineData(256, SpecRole.Healer)]  // Discipline Priest
    [InlineData(257, SpecRole.Healer)]  // Holy Priest
    [InlineData(264, SpecRole.Healer)]  // Restoration Shaman
    [InlineData(1473, SpecRole.Dps)]    // Augmentation Evoker (officially Dps despite support)
    [InlineData(258, SpecRole.Dps)]     // Shadow Priest
    [InlineData(70,  SpecRole.Dps)]     // Retribution Paladin
    [InlineData(1480, SpecRole.Dps)]    // Devourer DH
    public void Role_is_correct_for_known_specs(int id, SpecRole expectedRole)
    {
        var v = SpecMap.Lookup(id);
        Assert.NotNull(v);
        Assert.Equal(expectedRole, v!.Value.Role);
    }

    [Fact]
    public void Every_entry_has_a_role()
    {
        var allRoles = SpecMap.Known.Values.Select(v => v.Role).ToList();
        Assert.Equal(SpecMap.Known.Count, allRoles.Count);
        Assert.All(allRoles, r => Assert.Contains(r, new[] { SpecRole.Tank, SpecRole.Healer, SpecRole.Dps }));
    }

    [Fact]
    public void Has_tanks_healers_and_dps()
    {
        var byRole = SpecMap.Known.Values.GroupBy(v => v.Role).ToDictionary(g => g.Key, g => g.Count());
        // Sanity: at least a handful in each role bucket so we know the
        // mapping isn't accidentally collapsed onto one role.
        Assert.True(byRole.GetValueOrDefault(SpecRole.Tank) >= 6, "expected at least 6 tank specs");
        Assert.True(byRole.GetValueOrDefault(SpecRole.Healer) >= 6, "expected at least 6 healer specs");
        Assert.True(byRole.GetValueOrDefault(SpecRole.Dps) >= 20, "expected at least 20 dps specs");
    }

    [Fact]
    public void Demon_hunter_has_all_three_specs()
    {
        var specs = SpecMap.Known.Values
            .Where(v => v.Class == "Demon Hunter")
            .Select(v => v.Spec)
            .OrderBy(v => v)
            .ToArray();

        Assert.Equal(new[] { "Devourer", "Havoc", "Vengeance" }, specs);
    }
}
