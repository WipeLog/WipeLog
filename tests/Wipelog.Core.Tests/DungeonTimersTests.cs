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

using Wipelog.Core.Data;
using Xunit;

namespace Wipelog.Core.Tests;

public class DungeonTimersTests
{
    [Fact]
    public void Unknown_id_returns_null_par_time_and_null_upgrade_level()
    {
        // Table is empty by default — exercises the graceful-degradation
        // path the UI relies on for any dungeon we haven't populated yet.
        Assert.Null(DungeonTimers.GetParTime(999_999));
        Assert.Null(DungeonTimers.GetUpgradeLevel(999_999, TimeSpan.FromMinutes(20)));
    }

    private static readonly TimeSpan Par = TimeSpan.FromMinutes(30); // 1800s

    [Theory]
    // Just-in-time: exactly at par → +1 (≤ par boundary).
    [InlineData(1800, 1)]
    // 0.8 boundary: at par × 0.8 = 1440s, still +2 (≤). Just over → +1.
    [InlineData(1440, 2)]
    [InlineData(1441, 1)]
    // 0.6 boundary: at par × 0.6 = 1080s, still +3 (≤). Just over → +2.
    [InlineData(1080, 3)]
    [InlineData(1081, 2)]
    // Mid-tier samples.
    [InlineData(600, 3)]
    [InlineData(1200, 2)]
    [InlineData(1700, 1)]
    public void Compute_upgrade_level_for_in_time_finish(int durationSeconds, int expected)
    {
        Assert.Equal(expected,
            DungeonTimers.ComputeUpgradeLevel(Par, TimeSpan.FromSeconds(durationSeconds)));
    }

    [Theory]
    [InlineData(1801)]
    [InlineData(2400)]
    [InlineData(7200)]
    public void Compute_upgrade_level_returns_zero_when_over_par(int durationSeconds)
    {
        Assert.Equal(0,
            DungeonTimers.ComputeUpgradeLevel(Par, TimeSpan.FromSeconds(durationSeconds)));
    }
}
