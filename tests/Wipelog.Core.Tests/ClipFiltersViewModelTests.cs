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

using Wipelog.App.ViewModels;
using Wipelog.Core.Detection;
using Xunit;

namespace Wipelog.Core.Tests;

public class ClipFiltersViewModelTests
{
    [Fact]
    public void Default_state_builds_30_day_query_with_no_other_filters()
    {
        var vm = new ClipFiltersViewModel();
        var q = vm.BuildQuery();

        Assert.Null(q.ContentTypes);
        Assert.False(q.FavoritesOnly);
        Assert.Null(q.DisplayNameContains);
        Assert.NotNull(q.Since);
        Assert.True(q.Since!.Value > DateTimeOffset.UtcNow.AddDays(-31));
        Assert.True(q.Since!.Value < DateTimeOffset.UtcNow.AddDays(-29));
    }

    [Fact]
    public void Content_type_all_means_no_content_filter()
    {
        var vm = new ClipFiltersViewModel { ContentType = ContentTypeFilter.All };
        Assert.Null(vm.BuildQuery().ContentTypes);
    }

    [Fact]
    public void Content_type_mythic_plus_filters_to_m_plus()
    {
        var vm = new ClipFiltersViewModel { ContentType = ContentTypeFilter.MythicPlus };
        Assert.Equal(new[] { ContentType.MythicPlus }, vm.BuildQuery().ContentTypes);
    }

    [Fact]
    public void Content_type_raid_filters_to_raid_encounter()
    {
        var vm = new ClipFiltersViewModel { ContentType = ContentTypeFilter.Raid };
        Assert.Equal(new[] { ContentType.RaidEncounter }, vm.BuildQuery().ContentTypes);
    }

    [Fact]
    public void Content_type_arena_filters_to_arena_only()
    {
        var vm = new ClipFiltersViewModel { ContentType = ContentTypeFilter.Arena };
        Assert.Equal(new[] { ContentType.Arena }, vm.BuildQuery().ContentTypes);
    }

    [Fact]
    public void Content_type_solo_shuffle_filters_to_solo_shuffle_only()
    {
        var vm = new ClipFiltersViewModel { ContentType = ContentTypeFilter.SoloShuffle };
        Assert.Equal(new[] { ContentType.SoloShuffle }, vm.BuildQuery().ContentTypes);
    }

    [Fact]
    public void Content_type_battleground_folds_in_rated_and_unrated()
    {
        // The rail's Battlegrounds tab covers both regular and rated BGs.
        var vm = new ClipFiltersViewModel { ContentType = ContentTypeFilter.Battleground };
        Assert.Equal(
            new[] { ContentType.Battleground, ContentType.RatedBattleground },
            vm.BuildQuery().ContentTypes);
    }

    [Fact]
    public void Clip_filter_returns_manual_recordings()
    {
        // The "Clips" rail tab is the home for user-initiated Manual
        // recordings now — no more LIMIT 0 stub.
        var vm = new ClipFiltersViewModel { ContentType = ContentTypeFilter.Clip };
        var q = vm.BuildQuery();
        Assert.NotEqual(0, q.Limit);
        Assert.Equal(new[] { ContentType.Manual }, q.ContentTypes);
    }

    [Fact]
    public void Favorites_only_passes_through_to_query()
    {
        var vm = new ClipFiltersViewModel { FavoritesOnly = true };
        Assert.True(vm.BuildQuery().FavoritesOnly);
    }

    [Fact]
    public void Search_text_passes_through_after_trim_check()
    {
        var vm = new ClipFiltersViewModel { SearchText = "  ulgrax  " };
        Assert.Equal("  ulgrax  ", vm.BuildQuery().DisplayNameContains);
    }

    [Fact]
    public void Empty_search_text_means_no_name_filter()
    {
        var vm = new ClipFiltersViewModel { SearchText = "   " };
        Assert.Null(vm.BuildQuery().DisplayNameContains);
    }

    [Theory]
    [InlineData(DateRangeMode.Last7Days, 7)]
    [InlineData(DateRangeMode.Last30Days, 30)]
    [InlineData(DateRangeMode.Last90Days, 90)]
    public void Range_mode_preset_maps_to_relative_since(DateRangeMode mode, int daysAgo)
    {
        var vm = new ClipFiltersViewModel { RangeMode = mode };
        var q = vm.BuildQuery();
        Assert.NotNull(q.Since);
        Assert.Null(q.Before);
        var diff = DateTimeOffset.UtcNow - q.Since!.Value;
        Assert.True(Math.Abs(diff.TotalDays - daysAgo) < 1);
    }

    [Fact]
    public void Range_mode_all_time_means_no_date_filter()
    {
        var vm = new ClipFiltersViewModel { RangeMode = DateRangeMode.AllTime };
        var q = vm.BuildQuery();
        Assert.Null(q.Since);
        Assert.Null(q.Before);
    }

    [Fact]
    public void Custom_mode_with_inclusive_end_advances_Before_one_day()
    {
        // User picks "Mar 15" as the end of a range. The SQL upper bound is
        // exclusive (`< Before`), so the resulting Before must be Mar 16 at
        // midnight (in the original offset).
        var start = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(2));
        var end   = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.FromHours(2));
        var vm = new ClipFiltersViewModel
        {
            CustomStart = start,
            CustomEnd = end,
        };

        var q = vm.BuildQuery();

        Assert.Equal(DateRangeMode.Custom, vm.RangeMode);
        Assert.Equal(start, q.Since);
        Assert.Equal(end.AddDays(1), q.Before);
    }

    [Fact]
    public void Custom_mode_with_only_start_yields_open_ended_upper()
    {
        var vm = new ClipFiltersViewModel
        {
            CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var q = vm.BuildQuery();

        Assert.NotNull(q.Since);
        Assert.Null(q.Before);
    }

    [Fact]
    public void Setting_CustomStart_flips_mode_to_Custom()
    {
        var vm = new ClipFiltersViewModel { RangeMode = DateRangeMode.Last7Days };
        vm.CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(DateRangeMode.Custom, vm.RangeMode);
    }

    [Fact]
    public void Setting_CustomEnd_flips_mode_to_Custom()
    {
        var vm = new ClipFiltersViewModel { RangeMode = DateRangeMode.Last30Days };
        vm.CustomEnd = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(DateRangeMode.Custom, vm.RangeMode);
    }

    [Fact]
    public void Clearing_a_custom_date_does_not_flip_back_to_a_preset()
    {
        // Once the user is in Custom mode, clearing a single date keeps them
        // in Custom (with the remaining date as a one-sided range). Switching
        // back to a preset is an explicit user action.
        var vm = new ClipFiltersViewModel
        {
            CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            CustomEnd   = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
        };
        Assert.Equal(DateRangeMode.Custom, vm.RangeMode);

        vm.CustomEnd = null;

        Assert.Equal(DateRangeMode.Custom, vm.RangeMode);
        Assert.NotNull(vm.BuildQuery().Since);
        Assert.Null(vm.BuildQuery().Before);
    }

    [Theory]
    [InlineData(DateRangeMode.Last7Days,  "Last 7 days")]
    [InlineData(DateRangeMode.Last30Days, "Last 30 days")]
    [InlineData(DateRangeMode.Last90Days, "Last 90 days")]
    [InlineData(DateRangeMode.AllTime,    "All time")]
    public void RangeLabel_reflects_active_preset(DateRangeMode mode, string expected)
    {
        var vm = new ClipFiltersViewModel { RangeMode = mode };
        Assert.Equal(expected, vm.RangeLabel);
    }

    [Fact]
    public void RangeLabel_in_Custom_renders_From_To_pair()
    {
        var vm = new ClipFiltersViewModel
        {
            CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            CustomEnd   = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
        };
        Assert.Contains("Mar 1", vm.RangeLabel);
        Assert.Contains("Mar 15", vm.RangeLabel);
    }

    [Fact]
    public void RangeLabel_in_Custom_handles_one_sided_ranges()
    {
        var startOnly = new ClipFiltersViewModel
        {
            CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
        };
        Assert.Contains("Mar 1", startOnly.RangeLabel);
        Assert.Contains("onwards", startOnly.RangeLabel);

        var endOnly = new ClipFiltersViewModel
        {
            CustomEnd = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
        };
        Assert.Contains("Up to", endOnly.RangeLabel);
        Assert.Contains("Mar 15", endOnly.RangeLabel);
    }

    [Fact]
    public void Clear_filters_resets_search_favorites_and_range_but_keeps_content_type()
    {
        var vm = new ClipFiltersViewModel
        {
            SearchText = "boss",
            ContentType = ContentTypeFilter.Raid,
            FavoritesOnly = true,
            CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            CustomEnd = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero),
        };
        Assert.Equal(DateRangeMode.Custom, vm.RangeMode);

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal(string.Empty, vm.SearchText);
        Assert.False(vm.FavoritesOnly);
        Assert.Equal(DateRangeMode.Last30Days, vm.RangeMode);
        Assert.Null(vm.CustomStart);
        Assert.Null(vm.CustomEnd);
        // ContentType is the sidebar category, not a "filter" — survives Clear.
        Assert.Equal(ContentTypeFilter.Raid, vm.ContentType);
    }

    [Fact]
    public void Has_active_filters_flips_with_changes()
    {
        var vm = new ClipFiltersViewModel();
        Assert.False(vm.HasActiveFilters);

        vm.FavoritesOnly = true;
        Assert.True(vm.HasActiveFilters);

        vm.ClearFiltersCommand.Execute(null);
        Assert.False(vm.HasActiveFilters);

        vm.SearchText = "kara";
        Assert.True(vm.HasActiveFilters);

        vm.ClearFiltersCommand.Execute(null);
        Assert.False(vm.HasActiveFilters);

        vm.CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.True(vm.HasActiveFilters);
    }

    [Fact]
    public void Setting_property_does_not_throw_outside_dispatcher_context()
    {
        // Critical for testability: setting filter properties must not require
        // an Avalonia Dispatcher to be present.
        var vm = new ClipFiltersViewModel();
        vm.SearchText = "x";
        vm.ContentType = ContentTypeFilter.MythicPlus;
        vm.FavoritesOnly = true;
        vm.RangeMode = DateRangeMode.Last7Days;
        vm.CustomStart = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
