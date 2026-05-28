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

using System.ComponentModel;
using Wipelog.App.ViewModels;
using Xunit;

namespace Wipelog.Core.Tests;

public class DisplayOptionsViewModelTests
{
    [Fact]
    public void Defaults_show_all_deaths_and_encounters()
    {
        var vm = new DisplayOptionsViewModel();
        Assert.Equal(DeathsFilter.All, vm.Deaths);
        Assert.True(vm.EncountersVisible);
        Assert.True(vm.ShowAllDeaths);
        Assert.False(vm.ShowOnlyOwnDeaths);
        Assert.False(vm.HideDeaths);
    }

    [Fact]
    public void Deaths_filter_setter_round_trips_three_states()
    {
        var vm = new DisplayOptionsViewModel();

        vm.Deaths = DeathsFilter.Own;
        Assert.True(vm.ShowOnlyOwnDeaths);
        Assert.False(vm.ShowAllDeaths);
        Assert.False(vm.HideDeaths);

        vm.Deaths = DeathsFilter.None;
        Assert.True(vm.HideDeaths);
        Assert.False(vm.ShowAllDeaths);
        Assert.False(vm.ShowOnlyOwnDeaths);

        vm.Deaths = DeathsFilter.All;
        Assert.True(vm.ShowAllDeaths);
    }

    [Fact]
    public void EncountersVisible_toggles_independently_of_Deaths()
    {
        var vm = new DisplayOptionsViewModel { Deaths = DeathsFilter.None };
        vm.EncountersVisible = false;
        Assert.False(vm.EncountersVisible);
        Assert.Equal(DeathsFilter.None, vm.Deaths);

        vm.EncountersVisible = true;
        Assert.True(vm.EncountersVisible);
    }

    [Fact]
    public void Changing_Deaths_raises_derived_property_change_events()
    {
        var vm = new DisplayOptionsViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Deaths = DeathsFilter.Own;

        Assert.Contains(nameof(DisplayOptionsViewModel.Deaths), changed);
        Assert.Contains(nameof(DisplayOptionsViewModel.ShowAllDeaths), changed);
        Assert.Contains(nameof(DisplayOptionsViewModel.ShowOnlyOwnDeaths), changed);
        Assert.Contains(nameof(DisplayOptionsViewModel.HideDeaths), changed);
    }
}
