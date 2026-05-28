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

namespace Wipelog.Core.CombatLog;

/// <summary>
/// Splits the body of a combat-log line into fields. Top-level commas separate
/// fields; commas inside double-quoted strings or bracketed groups stay intact.
/// Bracketed groups are returned with their brackets, so the caller decides how
/// to parse arrays. Quoted scalar fields have their surrounding quotes stripped.
/// </summary>
public static class CombatLogFieldSplitter
{
    public static List<string> Split(ReadOnlySpan<char> body)
    {
        var result = new List<string>();
        if (body.IsEmpty)
        {
            return result;
        }

        bool inQuotes = false;
        int bracketDepth = 0;
        int fieldStart = 0;

        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    inQuotes = false;
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case '[':
                case '(':
                    bracketDepth++;
                    break;
                case ']':
                case ')':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    break;
                case ',':
                    if (bracketDepth == 0)
                    {
                        result.Add(MaterializeField(body[fieldStart..i]));
                        fieldStart = i + 1;
                    }
                    break;
            }
        }

        result.Add(MaterializeField(body[fieldStart..]));
        return result;
    }

    private static string MaterializeField(ReadOnlySpan<char> field)
    {
        if (field.Length >= 2 && field[0] == '"' && field[^1] == '"')
        {
            return field[1..^1].ToString();
        }
        return field.ToString();
    }
}
