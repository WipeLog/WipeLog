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

using Wipelog.Library;
using Xunit;

namespace Wipelog.Core.Tests;

public class TagNormalizationTests
{
    [Theory]
    [InlineData(" Prog ", "prog")]
    [InlineData("FARM", "farm")]
    [InlineData("two-night", "two-night")]
    [InlineData("clean_kill", "clean_kill")]
    [InlineData("first kill", "first kill")]
    public void Normalizes_to_trimmed_lowercase(string input, string expected)
    {
        Assert.True(TagNormalization.TryNormalize(input, out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hello!")]              // illegal char
    [InlineData("comma,nope")]
    [InlineData("toolongtoolongtoolongtoolongtoolongtoolong")]  // > 30
    public void Rejects_invalid_input(string? input)
    {
        Assert.False(TagNormalization.TryNormalize(input, out var _));
    }

    [Fact]
    public void Trimmed_length_is_what_is_measured()
    {
        var thirty = new string('a', 30);
        Assert.True(TagNormalization.TryNormalize("  " + thirty + "  ", out var actual));
        Assert.Equal(thirty, actual);
    }
}
