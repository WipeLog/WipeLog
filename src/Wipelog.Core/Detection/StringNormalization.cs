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

using System.Globalization;
using System.Text;

namespace Wipelog.Core.Detection;

public static class StringNormalization
{
    /// <summary>
    /// Normalizes a string for diacritic- and case-insensitive comparison.
    /// "Frostytånegl" and "Frostytanegl" both produce "frostytanegl".
    /// Uses Unicode normalization form D to decompose accents, then strips
    /// non-spacing marks (category Mn) and lowercases.
    /// </summary>
    public static string FoldForCompare(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().ToLowerInvariant();
    }
}
