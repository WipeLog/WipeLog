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

using Wipelog.App.Controls;
using Xunit;

namespace Wipelog.Core.Tests;

public class TombstoneRowLayoutTests
{
    [Fact]
    public void Empty_input_returns_empty_result()
    {
        var r = TombstoneRowLayout.Layout(Array.Empty<double>(), 600);
        Assert.Empty(r.Visibles);
        Assert.Empty(r.Badges);
    }

    [Fact]
    public void Zero_canvas_width_returns_empty_result()
    {
        var r = TombstoneRowLayout.Layout(new[] { 10.0, 50.0, 100.0 }, 0);
        Assert.Empty(r.Visibles);
        Assert.Empty(r.Badges);
    }

    [Fact]
    public void Edge_offsets_stay_in_bounds()
    {
        // Death at exactly 0 should keep x=0; death at canvasWidth should
        // clamp to canvasWidth (the renderer adjusts for glyph half-width).
        var r = TombstoneRowLayout.Layout(new[] { 0.0, 600.0 }, 600);
        Assert.Equal(2, r.Visibles.Count);
        Assert.Empty(r.Badges);
        Assert.Equal(0.0, r.Visibles[0].X);
        Assert.Equal(600.0, r.Visibles[1].X);
    }

    [Fact]
    public void Spaced_deaths_all_render_visibly()
    {
        var r = TombstoneRowLayout.Layout(new[] { 50.0, 200.0, 400.0 }, 600);
        Assert.Equal(3, r.Visibles.Count);
        Assert.Empty(r.Badges);
        Assert.Equal(new[] { 0, 1, 2 }, r.Visibles.Select(v => v.Index));
    }

    [Fact]
    public void Two_overlapping_deaths_render_side_by_side_no_badge()
    {
        // 8px gap — under the 12px threshold but it's only 2 of them so
        // both should be visible side-by-side with no badge.
        var r = TombstoneRowLayout.Layout(new[] { 100.0, 108.0 }, 600);
        Assert.Equal(2, r.Visibles.Count);
        Assert.Empty(r.Badges);
    }

    [Fact]
    public void Four_tight_cluster_collapses_to_two_visible_plus_badge_two()
    {
        // Four deaths within 8px each — single cluster of 4.
        var r = TombstoneRowLayout.Layout(
            new[] { 100.0, 105.0, 110.0, 115.0 }, 600);

        Assert.Equal(2, r.Visibles.Count);
        Assert.Single(r.Badges);
        Assert.Equal(2, r.Badges[0].Count);
        Assert.Equal(new[] { 2, 3 }, r.Badges[0].CollapsedIndices);
        Assert.Equal(new[] { 0, 1 }, r.Visibles.Select(v => v.Index));
    }

    [Fact]
    public void Five_tight_cluster_emits_badge_count_three()
    {
        var r = TombstoneRowLayout.Layout(
            new[] { 100.0, 105.0, 109.0, 113.0, 117.0 }, 600);

        Assert.Equal(2, r.Visibles.Count);
        Assert.Single(r.Badges);
        Assert.Equal(3, r.Badges[0].Count);
        Assert.Equal(new[] { 2, 3, 4 }, r.Badges[0].CollapsedIndices);
    }

    [Fact]
    public void Two_separate_clusters_each_resolved_independently()
    {
        // Cluster A at x≈100 with 3 deaths (collapses to 2 visible + badge),
        // cluster B at x≈400 with 1 death (single visible glyph).
        var r = TombstoneRowLayout.Layout(
            new[] { 100.0, 105.0, 110.0, 400.0 }, 600);

        Assert.Equal(3, r.Visibles.Count);
        Assert.Single(r.Badges);
        Assert.Equal(1, r.Badges[0].Count);
        Assert.Equal(new[] { 0, 1, 3 }, r.Visibles.Select(v => v.Index));
    }

    [Fact]
    public void Badge_x_is_clamped_into_canvas()
    {
        // Five-death cluster bleeds past the right edge — the badge marker
        // anchored to the third member's X (610) must clamp into the canvas.
        var r = TombstoneRowLayout.Layout(
            new[] { 595.0, 600.0, 610.0, 615.0, 620.0 }, 600);

        Assert.Single(r.Badges);
        Assert.True(r.Badges[0].X <= 600,
            $"Badge X = {r.Badges[0].X}, expected ≤ 600");
        // And visible glyphs also clamped.
        foreach (var v in r.Visibles)
        {
            Assert.True(v.X <= 600, $"Visible glyph X = {v.X} should clamp into 600.");
        }
    }
}
