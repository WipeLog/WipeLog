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

namespace Wipelog.Library;

/// <summary>
/// Tag input rules: trimmed, lowercased, max 30 chars, must match
/// <c>[a-z0-9-_ ]</c>. Empty/whitespace input rejected.
/// </summary>
public static class TagNormalization
{
    public const int MaxLength = 30;

    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var trimmed = input.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxLength) return false;
        var lower = trimmed.ToLowerInvariant();
        foreach (var c in lower)
        {
            bool ok = (c >= 'a' && c <= 'z')
                   || (c >= '0' && c <= '9')
                   || c == '-' || c == '_' || c == ' ';
            if (!ok) return false;
        }
        normalized = lower;
        return true;
    }
}
