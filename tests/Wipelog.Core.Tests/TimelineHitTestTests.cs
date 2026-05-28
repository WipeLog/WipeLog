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
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class TimelineHitTestTests
{
    // Canvas: 100px wide, clip: 10_000ms long → 1px = 100ms.
    // Events at offsets 1000, 5000, 9000 sit at x = 10, 50, 90.
    private static readonly IReadOnlyList<TimelineEvent> _events =
    [
        new PlayerDeathEvent(1000, "Player-A", "Alice", null, null, null),
        new PlayerDeathEvent(5000, "Player-B", "Bob",   null, null, null),
        new PlayerDeathEvent(9000, "Player-C", "Carol", null, null, null),
    ];

    [Fact]
    public void Click_within_tolerance_picks_the_event()
    {
        var hit = TimelineHitTest.FindNearest(12, 100, _events, 10_000);
        Assert.NotNull(hit);
        Assert.Equal(1000, hit!.OffsetMs);
    }

    [Fact]
    public void Click_far_from_any_marker_returns_null()
    {
        // x=30 is 20px from the closest (event at x=10 or x=50); tolerance is 6.
        var hit = TimelineHitTest.FindNearest(30, 100, _events, 10_000);
        Assert.Null(hit);
    }

    [Fact]
    public void Click_between_two_close_markers_picks_the_closer_one()
    {
        // Two markers at x=10 and x=12 (1000ms and 1200ms apart on this scale).
        var events = new TimelineEvent[]
        {
            new PlayerDeathEvent(1000, "Player-A", "Alice", null, null, null),
            new PlayerDeathEvent(1200, "Player-B", "Bob",   null, null, null),
        };
        var hit = TimelineHitTest.FindNearest(11.4, 100, events, 10_000);
        Assert.NotNull(hit);
        Assert.Equal(1200, hit!.OffsetMs);
    }

    [Fact]
    public void Zero_duration_returns_null()
    {
        var hit = TimelineHitTest.FindNearest(10, 100, _events, 0);
        Assert.Null(hit);
    }

    [Fact]
    public void Empty_events_returns_null()
    {
        var hit = TimelineHitTest.FindNearest(10, 100, Array.Empty<TimelineEvent>(), 10_000);
        Assert.Null(hit);
    }

    [Fact]
    public void Click_exactly_on_marker_hits_it()
    {
        var hit = TimelineHitTest.FindNearest(50, 100, _events, 10_000);
        Assert.NotNull(hit);
        Assert.Equal(5000, hit!.OffsetMs);
    }

    [Fact]
    public void Custom_tolerance_widens_the_hit_box()
    {
        // x=20 is 10px from marker at x=10 — outside default 6px, inside 12px tolerance.
        Assert.Null(TimelineHitTest.FindNearest(20, 100, _events, 10_000));
        var hit = TimelineHitTest.FindNearest(20, 100, _events, 10_000, toleranceXPx: 12);
        Assert.NotNull(hit);
        Assert.Equal(1000, hit!.OffsetMs);
    }
}
