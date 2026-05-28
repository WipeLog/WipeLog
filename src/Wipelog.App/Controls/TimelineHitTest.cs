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

using Wipelog.Core.Recording;

namespace Wipelog.App.Controls;

/// <summary>
/// Pure hit-test logic for timeline markers — split out from <see cref="TimelineCanvas"/>
/// so it can be unit-tested without spinning up Avalonia.
/// </summary>
public static class TimelineHitTest
{
    public const double DefaultToleranceXPx = 6;

    /// <param name="mouseX">Pointer X in canvas-local coordinates.</param>
    /// <param name="canvasWidth">Total drawn width in pixels.</param>
    /// <param name="events">Visible events (already filtered by toggles).</param>
    /// <param name="clipDurationMs">Clip duration in milliseconds.</param>
    /// <param name="toleranceXPx">Max horizontal pixel distance to count as a hit.</param>
    /// <returns>The closest event within tolerance, or null.</returns>
    public static TimelineEvent? FindNearest(
        double mouseX,
        double canvasWidth,
        IReadOnlyList<TimelineEvent> events,
        double clipDurationMs,
        double toleranceXPx = DefaultToleranceXPx)
    {
        if (canvasWidth <= 0 || clipDurationMs <= 0 || events.Count == 0) return null;
        TimelineEvent? best = null;
        double bestDist = double.MaxValue;
        foreach (var e in events)
        {
            var x = e.OffsetMs / clipDurationMs * canvasWidth;
            var d = Math.Abs(mouseX - x);
            if (d <= toleranceXPx && d < bestDist)
            {
                bestDist = d;
                best = e;
            }
        }
        return best;
    }
}
