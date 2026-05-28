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

public class TimelineTooltipFormatterTests
{
    // Recording started 2026-05-10 19:00:00 UTC. An event at offset 60s lands at 19:01:00.
    private static readonly DateTimeOffset _start = new(2026, 5, 10, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Death_with_source_and_spell_renders_full_killed_by_line()
    {
        var ev = new PlayerDeathEvent(
            OffsetMs: 60_000,
            PlayerGuid: "Player-1",
            PlayerName: "Osiria-Ravenholdt-EU",
            KilledBySource: "Chimaerus",
            KilledBySpell: "Consuming Miasma",
            KillingBlowAmount: null);
        var text = TimelineTooltipFormatter.Format(ev, _start);
        Assert.Equal(
            "19:01:00\nOsiria-Ravenholdt-EU\nKilled by Chimaerus → Consuming Miasma",
            text);
    }

    [Fact]
    public void Death_with_only_source_omits_spell_arrow()
    {
        var ev = new PlayerDeathEvent(
            OffsetMs: 60_000,
            PlayerGuid: "Player-1",
            PlayerName: "Aelara-DefiasBrotherhood-EU",
            KilledBySource: "Fyrakk",
            KilledBySpell: null,
            KillingBlowAmount: null);
        var text = TimelineTooltipFormatter.Format(ev, _start);
        Assert.EndsWith("Killed by Fyrakk", text);
        Assert.DoesNotContain("→", text);
    }

    [Fact]
    public void Death_with_neither_source_nor_spell_renders_cause_unknown()
    {
        var ev = new PlayerDeathEvent(
            OffsetMs: 60_000,
            PlayerGuid: "Player-1",
            PlayerName: "Mystery",
            KilledBySource: null,
            KilledBySpell: null,
            KillingBlowAmount: null);
        var text = TimelineTooltipFormatter.Format(ev, _start);
        Assert.EndsWith("Cause unknown", text);
    }

    [Fact]
    public void Boss_start_renders_time_then_encounter_name()
    {
        var ev = new BossStartEvent(120_000, 2902, "Chimaerus");
        var text = TimelineTooltipFormatter.Format(ev, _start);
        Assert.Equal("19:02:00\nChimaerus", text);
    }

    [Fact]
    public void Non_ASCII_player_name_renders_as_is()
    {
        var ev = new PlayerDeathEvent(
            OffsetMs: 0,
            PlayerGuid: "Player-1",
            PlayerName: "Frostytånegl-Ravenholdt-EU",
            KilledBySource: null,
            KilledBySpell: null,
            KillingBlowAmount: null);
        var text = TimelineTooltipFormatter.Format(ev, _start);
        Assert.Contains("Frostytånegl", text);
    }
}
