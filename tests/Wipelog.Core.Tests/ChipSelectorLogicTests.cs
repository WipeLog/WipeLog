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

using System.Collections;
using Wipelog.App.Controls;
using Xunit;

namespace Wipelog.Core.Tests;

public class ChipSelectorLogicTests
{
    [Fact]
    public void Select_moves_item_from_available_to_selected()
    {
        IList available = new List<string> { "a", "b", "c" };
        IList selected = new List<string> { "x" };

        ChipSelectorLogic.Select(available, selected, "b");

        Assert.Equal(new[] { "x", "b" }, selected.Cast<string>());
        Assert.Equal(new[] { "a", "c" }, available.Cast<string>());
    }

    [Fact]
    public void Deselect_moves_item_from_selected_to_available()
    {
        IList available = new List<string> { "a" };
        IList selected = new List<string> { "x", "y" };

        ChipSelectorLogic.Deselect(available, selected, "x");

        Assert.Equal(new[] { "y" }, selected.Cast<string>());
        Assert.Equal(new[] { "a", "x" }, available.Cast<string>());
    }

    [Fact]
    public void Select_is_idempotent_when_already_selected()
    {
        IList available = new List<string> { "b" };
        IList selected = new List<string> { "b" };

        // Defensive: an item present in both lists shouldn't duplicate into selected.
        ChipSelectorLogic.Select(available, selected, "b");

        Assert.Single(selected);
        Assert.Empty(available);
    }

    [Fact]
    public void CanAdd_reflects_available_items()
    {
        Assert.False(ChipSelectorLogic.CanAdd(null));
        Assert.False(ChipSelectorLogic.CanAdd(new List<string>()));
        Assert.True(ChipSelectorLogic.CanAdd(new List<string> { "a" }));
    }

    [Fact]
    public void Round_trips_an_item_out_and_back()
    {
        IList available = new List<string> { "a", "b" };
        IList selected = new List<string>();

        ChipSelectorLogic.Select(available, selected, "a");
        ChipSelectorLogic.Select(available, selected, "b");
        Assert.Empty(available);
        Assert.False(ChipSelectorLogic.CanAdd(available)); // empty → Add hidden

        ChipSelectorLogic.Deselect(available, selected, "a");
        Assert.Equal(new[] { "b" }, selected.Cast<string>());
        Assert.Equal(new[] { "a" }, available.Cast<string>());
        Assert.True(ChipSelectorLogic.CanAdd(available));
    }
}
