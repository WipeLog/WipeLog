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

using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

/// <summary>
/// Date-range filter. Last*Days are relative-to-now lower bounds with no
/// upper bound; AllTime drops the filter entirely; Custom uses
/// <see cref="ClipFiltersViewModel.CustomStart"/> and
/// <see cref="ClipFiltersViewModel.CustomEnd"/>.
/// </summary>
public enum DateRangeMode { Last7Days, Last30Days, Last90Days, AllTime, Custom }
// Battleground and Clip are visible sidebar entries but have no DB representation
// yet — selecting them yields an empty list. They exist so the sidebar layout
// can be shipped before the recording side learns how to produce those types.
public enum ContentTypeFilter { All, MythicPlus, Raid, Arena, SoloShuffle, Battleground, Clip }

public sealed partial class ClipFiltersViewModel : ViewModelBase
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(250);

    private DispatcherTimer? _debounce;

    public event EventHandler? FiltersChanged;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ContentTypeFilter _contentType = ContentTypeFilter.All;

    [ObservableProperty]
    private bool _favoritesOnly;

    [ObservableProperty]
    private DateRangeMode _rangeMode = DateRangeMode.Last30Days;

    /// <summary>Inclusive start of a custom range (local-time midnight from the calendar).</summary>
    [ObservableProperty]
    private DateTimeOffset? _customStart;

    /// <summary>Inclusive end of a custom range (local-time midnight from the calendar).</summary>
    [ObservableProperty]
    private DateTimeOffset? _customEnd;

    public bool HasActiveFilters =>
        !string.IsNullOrEmpty(SearchText)
        || FavoritesOnly
        || RangeMode != DateRangeMode.Last30Days
        || CustomStart is not null
        || CustomEnd is not null;

    /// <summary>
    /// Label shown on the date-range button in the filter bar. Reflects the
    /// active preset, or — for Custom mode — a compact rendering of the
    /// From/To dates ("Mar 1 - Mar 15", "Mar 1 onwards", "Up to Mar 15").
    /// </summary>
    public string RangeLabel => RangeMode switch
    {
        DateRangeMode.Last7Days => "Last 7 days",
        DateRangeMode.Last30Days => "Last 30 days",
        DateRangeMode.Last90Days => "Last 90 days",
        DateRangeMode.AllTime => "All time",
        DateRangeMode.Custom => FormatCustomRange(CustomStart, CustomEnd),
        _ => "All time",
    };

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        FavoritesOnly = false;
        RangeMode = DateRangeMode.Last30Days;
        CustomStart = null;
        CustomEnd = null;
        // ContentType is the sidebar's category, not a "filter" — leave it alone.
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(RangeLabel));
    }

    partial void OnSearchTextChanged(string value) { Bump(); OnPropertyChanged(nameof(HasActiveFilters)); }
    partial void OnContentTypeChanged(ContentTypeFilter value)
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        Bump();
    }
    partial void OnFavoritesOnlyChanged(bool value) { Bump(); OnPropertyChanged(nameof(HasActiveFilters)); }
    partial void OnRangeModeChanged(DateRangeMode value)
    {
        Bump();
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(RangeLabel));
    }
    partial void OnCustomStartChanged(DateTimeOffset? value)
    {
        // Picking a date means the user is composing a custom range; flip the
        // mode automatically so the button reflects it without a separate
        // "Custom" radio click. Clearing the date (value == null) doesn't
        // touch the mode — that's what ClearFilters does explicitly.
        if (value is not null) RangeMode = DateRangeMode.Custom;
        Bump();
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(RangeLabel));
    }
    partial void OnCustomEndChanged(DateTimeOffset? value)
    {
        if (value is not null) RangeMode = DateRangeMode.Custom;
        Bump();
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(RangeLabel));
    }

    public void Bump()
    {
        // Lazy/guarded: tests construct the VM without a Dispatcher and just
        // call BuildQuery directly.
        try
        {
            _debounce ??= CreateTimer();
            _debounce.Stop();
            _debounce.Start();
        }
        catch (InvalidOperationException)
        {
            // No Avalonia dispatcher (unit test context) — skip debounce.
        }
    }

    private DispatcherTimer CreateTimer()
    {
        var t = new DispatcherTimer { Interval = DebounceInterval };
        t.Tick += OnDebounceTick;
        return t;
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce!.Stop();
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public ClipQuery BuildQuery()
    {
        Wipelog.Core.Detection.ContentType[]? contentTypes = ContentType switch
        {
            ContentTypeFilter.Raid => new[] { Wipelog.Core.Detection.ContentType.RaidEncounter },
            ContentTypeFilter.MythicPlus => new[] { Wipelog.Core.Detection.ContentType.MythicPlus },
            ContentTypeFilter.Arena => new[] { Wipelog.Core.Detection.ContentType.Arena },
            ContentTypeFilter.SoloShuffle => new[] { Wipelog.Core.Detection.ContentType.SoloShuffle },
            // Rated + unrated BGs fold into one rail tab.
            ContentTypeFilter.Battleground => new[]
            {
                Wipelog.Core.Detection.ContentType.Battleground,
                Wipelog.Core.Detection.ContentType.RatedBattleground,
            },
            // The "Clips" rail tab is the home for free-form Manual recordings
            // (and, future, user-trimmed highlights from longer recordings).
            ContentTypeFilter.Clip => new[] { Wipelog.Core.Detection.ContentType.Manual },
            _ => null,
        };

        var (since, before) = RangeToBounds();

        return new ClipQuery(
            ContentTypes: contentTypes,
            FavoritesOnly: FavoritesOnly,
            DisplayNameContains: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText,
            Since: since,
            Before: before,
            Limit: 200,
            Offset: 0);
    }

    private (DateTimeOffset? Since, DateTimeOffset? Before) RangeToBounds()
    {
        return RangeMode switch
        {
            DateRangeMode.Last7Days => (DateTimeOffset.UtcNow.AddDays(-7), (DateTimeOffset?)null),
            DateRangeMode.Last30Days => (DateTimeOffset.UtcNow.AddDays(-30), (DateTimeOffset?)null),
            DateRangeMode.Last90Days => (DateTimeOffset.UtcNow.AddDays(-90), (DateTimeOffset?)null),
            DateRangeMode.AllTime => (null, (DateTimeOffset?)null),
            DateRangeMode.Custom => (
                CustomStart,
                // Inclusive end: a user-picked "Mar 15" should include all of
                // Mar 15, so the SQL upper bound (which is exclusive — `<`)
                // becomes the start of Mar 16 in the user's timezone.
                CustomEnd is { } end
                    ? new DateTimeOffset(end.Date, end.Offset).AddDays(1)
                    : null),
            _ => (null, (DateTimeOffset?)null),
        };
    }

    private static string FormatCustomRange(DateTimeOffset? start, DateTimeOffset? end)
    {
        static string F(DateTimeOffset d) => d.ToString("MMM d", CultureInfo.InvariantCulture);
        return (start, end) switch
        {
            (null, null) => "Custom",
            ({ } s, null) => $"{F(s)} onwards",
            (null, { } e) => $"Up to {F(e)}",
            ({ } s, { } e) => $"{F(s)} - {F(e)}",
        };
    }
}
