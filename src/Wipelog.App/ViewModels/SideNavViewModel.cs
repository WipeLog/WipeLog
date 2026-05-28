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
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Detection;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

/// <summary>One entry in the SukiSideMenu: a content-type category with a
/// rail icon, display name, and a live recording count.</summary>
public sealed partial class NavCategoryViewModel : ObservableObject
{
    public ContentTypeFilter Filter { get; }
    public string DisplayName { get; }
    public StreamGeometry? Icon { get; }

    // SukiSideMenuItem.Header is string-only, so fold the live count into the
    // label (e.g. "Mythic+   51"). Updates whenever Count changes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HeaderText))]
    private int _count;

    public string HeaderText => Count > 0 ? $"{DisplayName} {Count}" : DisplayName;

    public NavCategoryViewModel(ContentTypeFilter filter, string displayName, StreamGeometry? icon)
    {
        Filter = filter;
        DisplayName = displayName;
        Icon = icon;
    }
}

/// <summary>
/// Drives the labeled left sidebar: a status indicator that pulls from
/// <see cref="DaemonStatusViewModel"/>, four "Recordings" categories with
/// per-type counts, and two bottom "Settings" entries. Selecting a category
/// flips <see cref="ClipFiltersViewModel.ContentType"/>; the list view-model
/// reacts via its existing FiltersChanged subscription.
/// </summary>
public sealed partial class SideNavViewModel : ViewModelBase
{
    private readonly IClipRepository _repo;
    private readonly ClipFiltersViewModel _filters;
    private readonly ILogger<SideNavViewModel> _logger;
    private bool _suppressFilterSync;

    public DaemonStatusViewModel DaemonStatus { get; }

    /// <summary>The category entries shown in the side menu (bound to its
    /// ItemsSource). Order defines the visual order.</summary>
    public ObservableCollection<NavCategoryViewModel> Categories { get; }

    /// <summary>Two-way bound to SukiSideMenu.SelectedItem; drives the filter.</summary>
    [ObservableProperty]
    private NavCategoryViewModel? _selectedCategory;

    [ObservableProperty]
    private ContentTypeFilter _activeCategory = ContentTypeFilter.MythicPlus;

    [ObservableProperty]
    private int _mythicPlusCount;

    [ObservableProperty]
    private int _raidCount;

    [ObservableProperty]
    private int _arenaCount;

    [ObservableProperty]
    private int _soloShuffleCount;

    [ObservableProperty]
    private int _battlegroundCount;

    [ObservableProperty]
    private int _clipCount;

    public event EventHandler? OpenSettingsRequested;
    public event EventHandler? SceneStubRequested;

    public SideNavViewModel(
        IClipRepository repo,
        ClipFiltersViewModel filters,
        DaemonStatusViewModel daemonStatus,
        ILogger<SideNavViewModel> logger)
    {
        _repo = repo;
        _filters = filters;
        DaemonStatus = daemonStatus;
        _logger = logger;

        Categories = new ObservableCollection<NavCategoryViewModel>
        {
            new(ContentTypeFilter.MythicPlus,   "Mythic+",       Icon("IconRailMythicPlus")),
            new(ContentTypeFilter.Raid,         "Raids",         Icon("IconRailRaid")),
            new(ContentTypeFilter.Arena,        "Arena",         Icon("IconRailArena")),
            new(ContentTypeFilter.SoloShuffle,  "Solo Shuffle",  Icon("IconRailSoloShuffle")),
            new(ContentTypeFilter.Battleground, "Battlegrounds", Icon("IconRailBattleground")),
            new(ContentTypeFilter.Clip,         "Clips",         Icon("IconRailClips")),
        };

        // Drive the filter VM when the sidebar selection changes, and reflect
        // external filter changes back into the highlighted entry.
        ActiveCategory = filters.ContentType == ContentTypeFilter.All
            ? ContentTypeFilter.MythicPlus
            : filters.ContentType;
        SelectedCategory = CategoryFor(ActiveCategory);
        SyncFilterFromActiveCategory();
        filters.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClipFiltersViewModel.ContentType))
            {
                if (_suppressFilterSync) return;
                if (filters.ContentType != ContentTypeFilter.All
                    && filters.ContentType != ActiveCategory)
                {
                    ActiveCategory = filters.ContentType;
                }
            }
        };
    }

    partial void OnActiveCategoryChanged(ContentTypeFilter value)
    {
        SyncFilterFromActiveCategory();
        // Keep the menu highlight in step with external filter changes.
        var match = CategoryFor(value);
        if (match is not null && !ReferenceEquals(match, SelectedCategory))
            SelectedCategory = match;
    }

    partial void OnSelectedCategoryChanged(NavCategoryViewModel? value)
    {
        // SukiSideMenu clears selection transiently while reordering items;
        // ignore nulls so we don't bounce the filter to a default.
        if (value is not null) ActiveCategory = value.Filter;
    }

    private NavCategoryViewModel? CategoryFor(ContentTypeFilter filter) =>
        Categories.FirstOrDefault(c => c.Filter == filter);

    private static StreamGeometry? Icon(string key) =>
        Application.Current?.TryGetResource(key, ThemeVariant.Dark, out var v) == true
            && v is StreamGeometry g
                ? g
                : null;

    private void SyncFilterFromActiveCategory()
    {
        try
        {
            _suppressFilterSync = true;
            _filters.ContentType = ActiveCategory;
        }
        finally
        {
            _suppressFilterSync = false;
        }
    }

    public async Task RefreshCountsAsync(CancellationToken ct = default)
    {
        try
        {
            var counts = await _repo.GetCountsByContentTypeAsync(ct);
            MythicPlusCount    = counts.TryGetValue(ContentType.MythicPlus,        out var m) ? m : 0;
            RaidCount          = counts.TryGetValue(ContentType.RaidEncounter,     out var r) ? r : 0;
            ArenaCount         = counts.TryGetValue(ContentType.Arena,             out var a) ? a : 0;
            SoloShuffleCount   = counts.TryGetValue(ContentType.SoloShuffle,       out var s) ? s : 0;
            // The Battlegrounds rail tab folds in both rated and unrated entries —
            // the filter VM expands ContentTypeFilter.Battleground to that pair.
            BattlegroundCount  =
                (counts.TryGetValue(ContentType.Battleground,        out var b)  ? b  : 0) +
                (counts.TryGetValue(ContentType.RatedBattleground,   out var rb) ? rb : 0);
            // "Clips" rail tab → Manual recordings (no other content type
            // populates it yet; user-trimmed highlights are still future work).
            ClipCount = counts.TryGetValue(ContentType.Manual, out var mc) ? mc : 0;

            // Mirror into the side-menu entries.
            foreach (var cat in Categories)
            {
                cat.Count = cat.Filter switch
                {
                    ContentTypeFilter.MythicPlus   => MythicPlusCount,
                    ContentTypeFilter.Raid         => RaidCount,
                    ContentTypeFilter.Arena        => ArenaCount,
                    ContentTypeFilter.SoloShuffle  => SoloShuffleCount,
                    ContentTypeFilter.Battleground => BattlegroundCount,
                    ContentTypeFilter.Clip         => ClipCount,
                    _ => cat.Count,
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh sidebar counts.");
        }
    }

    [RelayCommand]
    private void OpenSettings() => OpenSettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenScene() => SceneStubRequested?.Invoke(this, EventArgs.Empty);
}
