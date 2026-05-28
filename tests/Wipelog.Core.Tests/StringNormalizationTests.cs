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

using Wipelog.Core.Detection;
using Xunit;

namespace Wipelog.Core.Tests;

public class StringNormalizationTests
{
    [Theory]
    [InlineData("Frostytånegl", "frostytanegl")]
    [InlineData("Frostytanegl", "frostytanegl")]
    [InlineData("FROSTYTANEGL", "frostytanegl")]
    [InlineData("FROSTYTÅNEGL", "frostytanegl")]
    [InlineData("Mëgëëndùs", "megeendus")]
    [InlineData("plain", "plain")]
    public void FoldForCompare_normalizes_case_and_diacritics(string input, string expected)
    {
        Assert.Equal(expected, StringNormalization.FoldForCompare(input));
    }

    [Fact]
    public void FoldForCompare_handles_null_and_empty()
    {
        Assert.Equal(string.Empty, StringNormalization.FoldForCompare(null));
        Assert.Equal(string.Empty, StringNormalization.FoldForCompare(""));
    }
}
