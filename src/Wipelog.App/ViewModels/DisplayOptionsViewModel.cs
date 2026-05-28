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

using CommunityToolkit.Mvvm.ComponentModel;

namespace Wipelog.App.ViewModels;

public enum DeathsFilter
{
    /// <summary>Show tombstones for every party member.</summary>
    All,
    /// <summary>Show tombstones only for the recording player.</summary>
    Own,
    /// <summary>Hide all tombstones. Boss labels still render if EncountersVisible.</summary>
    None,
}

/// <summary>
/// Visual toggles that affect the <see cref="ScrubWithTombstones"/> control but
/// don't change which clips appear in the table. Kept in a separate VM from
/// <see cref="ClipFiltersViewModel"/> so the database-query path is decoupled
/// from in-clip visualization preferences.
/// </summary>
public sealed partial class DisplayOptionsViewModel : ViewModelBase
{
    [ObservableProperty]
    private DeathsFilter _deaths = DeathsFilter.All;

    [ObservableProperty]
    private bool _encountersVisible = true;

    public bool ShowAllDeaths => Deaths == DeathsFilter.All;
    public bool ShowOnlyOwnDeaths => Deaths == DeathsFilter.Own;
    public bool HideDeaths => Deaths == DeathsFilter.None;

    partial void OnDeathsChanged(DeathsFilter value)
    {
        OnPropertyChanged(nameof(ShowAllDeaths));
        OnPropertyChanged(nameof(ShowOnlyOwnDeaths));
        OnPropertyChanged(nameof(HideDeaths));
    }
}
