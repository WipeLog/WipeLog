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

using Wipelog.App.ViewModels;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipPovFormatterTests
{
    [Fact]
    public void Full_class_spec_and_name_renders_as_name_dot_spec_class()
    {
        Assert.Equal("Aelara · Frost Mage",
            ClipPovFormatter.FormatPov("Mage", "Frost", "Aelara"));
    }

    [Fact]
    public void Missing_spec_drops_to_name_and_class()
    {
        Assert.Equal("Aelara · Mage",
            ClipPovFormatter.FormatPov("Mage", null, "Aelara"));
        Assert.Equal("Aelara · Mage",
            ClipPovFormatter.FormatPov("Mage", "", "Aelara"));
    }

    [Fact]
    public void Missing_class_drops_to_name_only()
    {
        Assert.Equal("Aelara",
            ClipPovFormatter.FormatPov(null, "Frost", "Aelara"));
        Assert.Equal("Aelara",
            ClipPovFormatter.FormatPov("", "Frost", "Aelara"));
    }

    [Fact]
    public void Missing_name_falls_back_to_class_only_text()
    {
        // No name, but class is known — still show class so the row is useful.
        Assert.Equal("Frost Mage",
            ClipPovFormatter.FormatPov("Mage", "Frost", null));
    }

    [Fact]
    public void All_missing_returns_em_dash()
    {
        Assert.Equal("—", ClipPovFormatter.FormatPov(null, null, null));
        Assert.Equal("—", ClipPovFormatter.FormatPov("", "", ""));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData(18, "+18")]
    [InlineData(2, "+2")]
    public void Format_key_renders_plus_n_for_M_plus(int? key, string expected)
    {
        Assert.Equal(expected, ClipPovFormatter.FormatKey(key));
    }

    [Fact]
    public void Class_color_hex_delegates_to_ClassColors()
    {
        Assert.Equal("#3FC7EB", ClipPovFormatter.ClassColorHex("Mage"));
        Assert.Equal("#80848e", ClipPovFormatter.ClassColorHex(null));
    }
}
