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

namespace Wipelog.App.Controls;

/// <summary>
/// Pure layout helper for the boss-name labels under boss-start markers.
/// Labels extend right from the bar's X. If two adjacent labels would
/// overlap, the earlier one is truncated to fit before the next bar; if
/// the truncation would leave fewer than <see cref="MinLabelChars"/>
/// characters, the earlier label is hidden entirely.
///
/// Truncation appends an ellipsis ("Sk…"). The absolute cap is 18 chars —
/// even on a wide canvas with one boss, we don't sprawl across the whole
/// screen.
/// </summary>
public static class BossLabelLayout
{
    public const int MaxLabelChars = 18;
    public const int MinLabelChars = 3;
    private const double GapPx = 4;

    public static string[] AssignVisibleLabels(
        IReadOnlyList<(int OffsetMs, string Name)> bosses,
        double canvasWidthPx,
        double clipDurationMs,
        double charWidthPx)
    {
        var result = new string[bosses.Count];
        if (canvasWidthPx <= 0 || clipDurationMs <= 0 || charWidthPx <= 0)
            return result;

        for (int i = 0; i < bosses.Count; i++)
        {
            var (offsetMs, name) = bosses[i];
            var x = offsetMs / clipDurationMs * canvasWidthPx;
            double nextX = i + 1 < bosses.Count
                ? bosses[i + 1].OffsetMs / clipDurationMs * canvasWidthPx
                : canvasWidthPx;

            var availablePx = System.Math.Max(0, nextX - x - GapPx);
            int maxCharsHere = (int)System.Math.Floor(availablePx / charWidthPx);
            maxCharsHere = System.Math.Min(maxCharsHere, MaxLabelChars);

            result[i] = Truncate(name ?? string.Empty, maxCharsHere);
        }
        return result;
    }

    private static string Truncate(string name, int maxChars)
    {
        if (maxChars < MinLabelChars) return string.Empty;
        if (name.Length <= maxChars) return name;
        // -1 for the ellipsis; if that drops below MinLabelChars, hide.
        var keep = maxChars - 1;
        return keep < MinLabelChars - 1 ? string.Empty : name[..keep] + "…";
    }
}
