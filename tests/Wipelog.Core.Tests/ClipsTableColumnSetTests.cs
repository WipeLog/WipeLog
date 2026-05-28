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

public class ClipsTableColumnSetTests
{
    [Theory]
    [InlineData(ContentTypeFilter.MythicPlus, ClipsTableVariant.MythicPlus)]
    [InlineData(ContentTypeFilter.Raid,       ClipsTableVariant.Raid)]
    [InlineData(ContentTypeFilter.All,        ClipsTableVariant.All)]
    public void Content_type_maps_to_variant(ContentTypeFilter input, ClipsTableVariant expected)
    {
        Assert.Equal(expected, ClipsTableColumnSet.For(input));
    }
}
