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

using System.Collections.Generic;
using System.Linq;
using Wipelog.Core.Detection;

namespace Wipelog.App.ViewModels;

/// <summary>
/// Pure mapping between persisted <see cref="RecordingFilters.RaidDifficulties"/>
/// and the two collections that back the difficulty ChipSelector. Extracted from
/// <see cref="SettingsViewModel"/> so it can be unit-tested without instantiating
/// the (LibVLC/Dispatcher-bound) view model.
/// </summary>
public static class RaidDifficultyChips
{
    /// <summary>
    /// Split <paramref name="all"/> into the recorded tiers (chips) and the rest
    /// (Add flyout), in canonical order. An empty <paramref name="saved"/> set is
    /// treated as "all selected" so the daemon's record-everything default shows
    /// up as four selected chips rather than an empty strip.
    /// </summary>
    public static void Populate(
        IReadOnlyList<RaidDifficulty> all,
        IReadOnlyCollection<RaidDifficulty> saved,
        IList<RaidDifficulty> selected,
        IList<RaidDifficulty> available)
    {
        var effective = saved.Count == 0 ? all : saved;
        selected.Clear();
        available.Clear();
        foreach (var tier in all)
        {
            if (effective.Contains(tier)) selected.Add(tier);
            else available.Add(tier);
        }
    }

    /// <summary>
    /// Project the selected chips back to a persisted difficulty list in
    /// canonical order, regardless of the add/remove sequence the user took.
    /// </summary>
    public static IReadOnlyList<RaidDifficulty> Project(
        IReadOnlyList<RaidDifficulty> all,
        ICollection<RaidDifficulty> selected) =>
        all.Where(selected.Contains).ToArray();
}
