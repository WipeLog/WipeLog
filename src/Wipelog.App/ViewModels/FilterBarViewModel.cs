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

namespace Wipelog.App.ViewModels;

/// <summary>
/// Composes <see cref="ClipFiltersViewModel"/> and <see cref="DisplayOptionsViewModel"/>
/// into a single binding source for <c>FilterBarView</c>. Two distinct
/// concerns — DB-query filters and timeline visualization toggles — share one
/// horizontal bar so the user thinks of them as one cluster of controls; the
/// view-model keeps them cleanly separable.
/// </summary>
public sealed class FilterBarViewModel : ViewModelBase
{
    public ClipFiltersViewModel Filters { get; }
    public DisplayOptionsViewModel Display { get; }

    public FilterBarViewModel(ClipFiltersViewModel filters, DisplayOptionsViewModel display)
    {
        Filters = filters;
        Display = display;
    }
}
