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

using System.Collections.ObjectModel;
using Wipelog.App.ViewModels;
using Wipelog.Core.Detection;
using Xunit;

namespace Wipelog.Core.Tests;

public class RaidDifficultyChipsTests
{
    private static readonly IReadOnlyList<RaidDifficulty> All = new[]
    {
        RaidDifficulty.LFR, RaidDifficulty.Normal, RaidDifficulty.Heroic, RaidDifficulty.Mythic,
    };

    [Fact]
    public void Populate_splits_saved_into_selected_and_available_in_canonical_order()
    {
        var selected = new ObservableCollection<RaidDifficulty>();
        var available = new ObservableCollection<RaidDifficulty>();

        RaidDifficultyChips.Populate(
            All,
            new[] { RaidDifficulty.Heroic, RaidDifficulty.Mythic },
            selected, available);

        Assert.Equal(new[] { RaidDifficulty.Heroic, RaidDifficulty.Mythic }, selected);
        Assert.Equal(new[] { RaidDifficulty.LFR, RaidDifficulty.Normal }, available);
    }

    [Fact]
    public void Populate_treats_empty_saved_set_as_all_selected()
    {
        var selected = new ObservableCollection<RaidDifficulty>();
        var available = new ObservableCollection<RaidDifficulty>();

        RaidDifficultyChips.Populate(All, Array.Empty<RaidDifficulty>(), selected, available);

        Assert.Equal(All, selected);
        Assert.Empty(available);
    }

    [Fact]
    public void Project_returns_selected_in_canonical_order()
    {
        // User added chips out of order; projection still canonical.
        var selected = new ObservableCollection<RaidDifficulty>
        {
            RaidDifficulty.Mythic, RaidDifficulty.LFR,
        };

        var projected = RaidDifficultyChips.Project(All, selected);

        Assert.Equal(new[] { RaidDifficulty.LFR, RaidDifficulty.Mythic }, projected);
    }

    [Fact]
    public void Populate_then_Project_round_trips()
    {
        var selected = new ObservableCollection<RaidDifficulty>();
        var available = new ObservableCollection<RaidDifficulty>();
        var saved = new[] { RaidDifficulty.Normal, RaidDifficulty.Mythic };

        RaidDifficultyChips.Populate(All, saved, selected, available);
        var projected = RaidDifficultyChips.Project(All, selected);

        Assert.Equal(saved, projected);
    }
}
