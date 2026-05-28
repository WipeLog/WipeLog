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
using CommunityToolkit.Mvvm.ComponentModel;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using Wipelog.App.Services;
using Wipelog.Core.Preferences;

namespace Wipelog.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly LibraryWatcher _watcher;
    private readonly UserPreferencesService _userPrefsService;

    public ClipFiltersViewModel Filters { get; }
    public ClipListViewModel ClipList { get; }
    public PlayerViewModel Player { get; }
    public TimelineViewModel Timeline { get; }
    public DaemonStatusViewModel DaemonStatus { get; }
    public SideNavViewModel SideNav { get; }
    public FilterBarViewModel FilterBar { get; }
    public ClipDetailsViewModel ClipDetails { get; }

    // Bound to the SukiToastHost / SukiDialogHost in SukiWindow.
    public ISukiDialogManager DialogManager { get; }
    public ISukiToastManager ToastManager { get; }

    [ObservableProperty]
    private bool _isShowingSettings;

    [ObservableProperty]
    private SettingsViewModel? _settings;

    public MainWindowViewModel(
        ClipFiltersViewModel filters,
        ClipListViewModel clipList,
        PlayerViewModel player,
        TimelineViewModel timeline,
        LibraryWatcher watcher,
        DaemonStatusViewModel daemonStatus,
        SideNavViewModel sideNav,
        FilterBarViewModel filterBar,
        ClipDetailsViewModel clipDetails,
        ISukiDialogManager dialogManager,
        ISukiToastManager toastManager,
        UserPreferencesService userPrefsService)
    {
        Filters = filters;
        ClipList = clipList;
        Player = player;
        Timeline = timeline;
        _watcher = watcher;
        _userPrefsService = userPrefsService;
        DaemonStatus = daemonStatus;
        SideNav = sideNav;
        FilterBar = filterBar;
        ClipDetails = clipDetails;
        DialogManager = dialogManager;
        ToastManager = toastManager;

        // Sidebar counts mirror filesystem state, so refresh on the same
        // signal the clip list uses — its IsLoading flips false at the tail
        // of every RefreshAsync.
        ClipList.PropertyChanged += OnClipListPropertyChanged;
    }

    public string Title => "Wipelog";

    public string OutputDirectory
    {
        get
        {
            var configured = _userPrefsService.Load().Daemon.OutputDirectory;
            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "wow")
                : configured;
        }
    }

    public async Task InitializeAsync()
    {
        await ClipList.InitializeAsync();
        await SideNav.RefreshCountsAsync();
        await _watcher.StartAsync(OutputDirectory);
    }

    private void OnClipListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipListViewModel.IsLoading) && !ClipList.IsLoading)
        {
            _ = SideNav.RefreshCountsAsync();
        }
    }
}
