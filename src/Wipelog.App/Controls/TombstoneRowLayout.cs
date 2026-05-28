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
/// Pure layout helper for the single-row tombstone strip under the scrub bar.
/// Walks left-to-right and groups deaths whose pixel positions are within
/// <see cref="CollisionThresholdPx"/> of each other into clusters. A cluster of
/// 1 or 2 deaths renders both glyphs; a cluster of 3+ renders the first 2 and
/// emits a single "+N" badge for the remainder. The badge tooltip lists every
/// collapsed death so the user doesn't lose information — they just need to
/// hover.
///
/// This replaces the Stage 5.1 <c>MarkerLayout</c> two-row stacking. The
/// tombstone strip lives in a fixed 22 px band under the scrub bar with no
/// vertical room for a second row, so collisions resolve horizontally only.
/// </summary>
public static class TombstoneRowLayout
{
    public const double CollisionThresholdPx = 12.0;
    public const int MaxVisiblePerCluster = 2;

    public sealed record Visible(int Index, double X);
    public sealed record Badge(double X, int Count, IReadOnlyList<int> CollapsedIndices);
    public sealed record Result(IReadOnlyList<Visible> Visibles, IReadOnlyList<Badge> Badges);

    /// <param name="sortedXsPx">Per-death pixel X positions, sorted ascending.</param>
    /// <param name="canvasWidthPx">Total drawable width — Xs are clamped into it.</param>
    /// <param name="collisionThresholdPx">
    /// Two consecutive deaths whose X gap is smaller than this fall into the
    /// same cluster. Defaults to ~one glyph width.
    /// </param>
    public static Result Layout(
        IReadOnlyList<double> sortedXsPx,
        double canvasWidthPx,
        double collisionThresholdPx = CollisionThresholdPx)
    {
        var visibles = new List<Visible>();
        var badges = new List<Badge>();
        if (sortedXsPx.Count == 0 || canvasWidthPx <= 0)
        {
            return new Result(visibles, badges);
        }

        var current = new List<int> { 0 };
        for (int i = 1; i < sortedXsPx.Count; i++)
        {
            var prev = sortedXsPx[current[^1]];
            if (sortedXsPx[i] - prev < collisionThresholdPx)
            {
                current.Add(i);
            }
            else
            {
                EmitCluster(current, sortedXsPx, canvasWidthPx, visibles, badges);
                current = new List<int> { i };
            }
        }
        EmitCluster(current, sortedXsPx, canvasWidthPx, visibles, badges);
        return new Result(visibles, badges);
    }

    private static void EmitCluster(
        List<int> indices,
        IReadOnlyList<double> xs,
        double canvasWidth,
        List<Visible> visibles,
        List<Badge> badges)
    {
        if (indices.Count <= MaxVisiblePerCluster)
        {
            foreach (var i in indices)
            {
                visibles.Add(new Visible(i, ClampX(xs[i], canvasWidth)));
            }
            return;
        }

        for (int n = 0; n < MaxVisiblePerCluster; n++)
        {
            var i = indices[n];
            visibles.Add(new Visible(i, ClampX(xs[i], canvasWidth)));
        }

        var collapsed = indices.Skip(MaxVisiblePerCluster).ToList();
        // Position the badge at the third member's X — visually "right after"
        // the last visible glyph. Cluster members are dense enough that the
        // exact spot inside the cluster barely matters; the user sees the
        // count and reads the tooltip.
        var badgeX = ClampX(xs[indices[MaxVisiblePerCluster]], canvasWidth);
        badges.Add(new Badge(badgeX, collapsed.Count, collapsed));
    }

    private static double ClampX(double x, double width) =>
        System.Math.Clamp(x, 0, System.Math.Max(0, width));
}
