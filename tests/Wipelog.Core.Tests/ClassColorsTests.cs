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

public class ClassColorsTests
{
    [Theory]
    [InlineData("Mage", "#3FC7EB")]
    [InlineData("Warlock", "#8788EE")]
    [InlineData("Death Knight", "#C41E3A")]
    [InlineData("Demon Hunter", "#A330C9")]
    [InlineData("Evoker", "#33937F")]
    [InlineData("Priest", "#FFFFFF")]
    public void Known_class_returns_canonical_hex(string className, string expected)
    {
        Assert.Equal(expected, ClassColors.GetHex(className));
    }

    [Fact]
    public void Unknown_class_returns_neutral_grey()
    {
        Assert.Equal("#80848e", ClassColors.GetHex("Tinkerer"));
    }

    [Fact]
    public void Null_returns_neutral_grey()
    {
        Assert.Equal("#80848e", ClassColors.GetHex(null));
    }

    [Fact]
    public void Hex_table_covers_all_thirteen_classes()
    {
        // Sanity: if a future expansion drops or renames a class we want this
        // test to point at the table directly.
        Assert.Equal(13, ClassColors.Hex.Count);
    }
}
