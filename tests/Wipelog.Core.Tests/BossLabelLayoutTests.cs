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

public class BossLabelLayoutTests
{
    // Canvas 1000 px, clip 10 min. 1 char ≈ 6 px → 60ish chars across the
    // canvas in the worst case, so well-spaced bosses keep their full name.
    private const double CanvasWidth = 1000;
    private const double ClipDurationMs = 600_000;
    private const double CharWidth = 6;

    [Fact]
    public void Single_boss_keeps_its_name_when_short()
    {
        var labels = BossLabelLayout.AssignVisibleLabels(
            new[] { (60_000, "Skarmorak") },
            CanvasWidth, ClipDurationMs, CharWidth);
        Assert.Equal("Skarmorak", labels[0]);
    }

    [Fact]
    public void Long_name_caps_at_18_chars_with_ellipsis()
    {
        // 20-char name, plenty of horizontal room. Cap kicks in.
        var labels = BossLabelLayout.AssignVisibleLabels(
            new[] { (60_000, "Aaaaaaaaaaaaaaaaaaaa") },
            CanvasWidth, ClipDurationMs, CharWidth);
        Assert.Equal(18, labels[0].Length);
        Assert.EndsWith("…", labels[0]);
    }

    [Fact]
    public void Adjacent_bosses_truncate_the_earlier_label_to_fit()
    {
        // Boss A at 5 min, Boss B at 5min+5s. Gap is ~5s → 8.3px → fits ~1
        // char. That's below MinLabelChars so A is hidden, B keeps its name.
        var labels = BossLabelLayout.AssignVisibleLabels(
            new[] {
                (300_000, "Speaker Brinthe"),
                (305_000, "Skarmorak"),
            },
            CanvasWidth, ClipDurationMs, CharWidth);
        Assert.Equal(string.Empty, labels[0]);
        Assert.Equal("Skarmorak", labels[1]);
    }

    [Fact]
    public void Moderately_spaced_bosses_both_keep_names_but_earlier_may_truncate()
    {
        // Boss A at 1 min, Boss B at 5 min. 4-minute gap → 400 px → ~66 chars
        // available — both fit comfortably.
        var labels = BossLabelLayout.AssignVisibleLabels(
            new[] {
                (60_000, "Skarmorak"),
                (300_000, "Speaker Brinthe"),
            },
            CanvasWidth, ClipDurationMs, CharWidth);
        Assert.Equal("Skarmorak", labels[0]);
        Assert.Equal("Speaker Brinthe", labels[1]);
    }

    [Fact]
    public void Earlier_label_truncates_with_ellipsis_when_space_is_tight()
    {
        // Boss A at 0s, Boss B at 60s. Gap = 100 px / 6px-per-char ≈ 16 chars
        // - 1 for safety gap = ~15. So a 14-char name fits, an 18-char name
        // needs to truncate to "Aaaaaaaaaaaaa…" (14 chars total).
        var labels = BossLabelLayout.AssignVisibleLabels(
            new[] {
                (0, "Aaaaaaaaaaaaaaaaaa"), // 18 chars
                (60_000, "Skarmorak"),
            },
            CanvasWidth, ClipDurationMs, CharWidth);
        Assert.EndsWith("…", labels[0]);
        Assert.True(labels[0].Length < 18);
        Assert.Equal("Skarmorak", labels[1]);
    }

    [Fact]
    public void Empty_input_returns_empty_array()
    {
        var labels = BossLabelLayout.AssignVisibleLabels(
            Array.Empty<(int, string)>(),
            CanvasWidth, ClipDurationMs, CharWidth);
        Assert.Empty(labels);
    }

    [Fact]
    public void Zero_canvas_returns_default_filled_array()
    {
        var labels = BossLabelLayout.AssignVisibleLabels(
            new[] { (60_000, "Skarmorak") }, 0, ClipDurationMs, CharWidth);
        Assert.Equal(string.Empty, labels[0] ?? string.Empty);
    }

    [Fact]
    public void Null_boss_name_renders_as_empty_string()
    {
        var labels = BossLabelLayout.AssignVisibleLabels(
            new[] { (60_000, (string)null!) },
            CanvasWidth, ClipDurationMs, CharWidth);
        Assert.Equal(string.Empty, labels[0]);
    }
}
