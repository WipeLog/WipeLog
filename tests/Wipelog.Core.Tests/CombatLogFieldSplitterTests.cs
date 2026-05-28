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

public class CombatLogFieldSplitterTests
{
    [Fact]
    public void Splits_plain_csv()
    {
        var fields = CombatLogFieldSplitter.Split("a,b,c");
        Assert.Equal(new[] { "a", "b", "c" }, fields);
    }

    [Fact]
    public void Keeps_commas_inside_quoted_string()
    {
        var fields = CombatLogFieldSplitter.Split("1,\"Brood of Nashal, the Eternal\",2");
        Assert.Equal(3, fields.Count);
        Assert.Equal("1", fields[0]);
        Assert.Equal("Brood of Nashal, the Eternal", fields[1]);
        Assert.Equal("2", fields[2]);
    }

    [Fact]
    public void Keeps_bracketed_array_with_brackets_intact()
    {
        var fields = CombatLogFieldSplitter.Split("1,2,[9,135,136,148],3");
        Assert.Equal(new[] { "1", "2", "[9,135,136,148]", "3" }, fields);
    }

    [Fact]
    public void Preserves_nested_brackets_verbatim()
    {
        var fields = CombatLogFieldSplitter.Split("1,[a,[b,c],d],2");
        Assert.Equal(3, fields.Count);
        Assert.Equal("1", fields[0]);
        Assert.Equal("[a,[b,c],d]", fields[1]);
        Assert.Equal("2", fields[2]);
    }

    [Fact]
    public void Empty_field_yields_empty_string()
    {
        var fields = CombatLogFieldSplitter.Split("a,,b");
        Assert.Equal(new[] { "a", "", "b" }, fields);
    }

    [Fact]
    public void Nil_is_returned_literally()
    {
        var fields = CombatLogFieldSplitter.Split("a,nil,b");
        Assert.Equal(new[] { "a", "nil", "b" }, fields);
    }
}
