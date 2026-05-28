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

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SukiUI.Controls;
using SukiUI.Dialogs;
using Wipelog.App.Services;
using Wipelog.App.ViewModels;
using Wipelog.Library;

namespace Wipelog.App.Views;

public partial class MainWindow : SukiWindow
{
    private readonly WindowStateService _windowStateService = new();
    private bool _allowClose;
    private SideNavViewModel? _wiredSideNav;
    private ISukiDialogManager? _wiredDialogManager;
    private MainWindowViewModel? _wiredVm;
    private bool _dialogOpen;

    public MainWindow()
    {
        InitializeComponent();
        ApplyPersistedState();
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
        // Tunnel KeyDown routes to KeyboardDevice.FocusedElement; without an
        // initial focus owner the event is dropped and Space/M/J/K never reach
        // our handler. Window.Focus() doesn't make the Window itself the
        // routing target on Avalonia — focus has to land on a descendant. We
        // anchor it on a hidden focusable Button defined in the axaml. Hook
        // both Loaded and Opened — Loaded fires when the tree is built but
        // before the window has been shown, and on some platforms .Focus()
        // is only effective once the window is realized.
        Loaded += OnInitialFocus;
        Opened += OnInitialFocus;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wiredSideNav is not null)
        {
            _wiredSideNav.OpenSettingsRequested -= OnOpenSettingsRequested;
            _wiredSideNav.SceneStubRequested -= OnSceneStubRequested;
            _wiredSideNav = null;
        }
        if (_wiredDialogManager is not null)
        {
            _wiredDialogManager.OnDialogShown -= OnDialogShown;
            _wiredDialogManager.OnDialogDismissed -= OnDialogDismissed;
            _wiredDialogManager = null;
        }
        if (_wiredVm is not null)
        {
            _wiredVm.PropertyChanged -= OnVmPropertyChanged;
            _wiredVm = null;
        }
        if (Vm is { } vm)
        {
            _wiredSideNav = vm.SideNav;
            _wiredSideNav.OpenSettingsRequested += OnOpenSettingsRequested;
            _wiredSideNav.SceneStubRequested += OnSceneStubRequested;
            _wiredDialogManager = vm.DialogManager;
            _wiredDialogManager.OnDialogShown += OnDialogShown;
            _wiredDialogManager.OnDialogDismissed += OnDialogDismissed;
            _wiredVm = vm;
            _wiredVm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    // The LibVLC VideoView renders on a native surface that sits ABOVE Avalonia's
    // composited content (the classic "airspace" limitation), so any overlay —
    // a SukiUI dialog or the full-page Settings — gets clipped wherever it covers
    // the player. Hide the native surface while an overlay is up; the MediaPlayer
    // keeps running and the frame returns when the overlay closes.
    private void OnDialogShown(object? sender, SukiUI.Dialogs.SukiDialogManagerEventArgs e)
    {
        _dialogOpen = true;
        UpdateVideoSurfaceVisibility();
    }

    private void OnDialogDismissed(object? sender, SukiUI.Dialogs.SukiDialogManagerEventArgs e)
    {
        _dialogOpen = false;
        UpdateVideoSurfaceVisibility();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsShowingSettings))
            UpdateVideoSurfaceVisibility();
    }

    private void UpdateVideoSurfaceVisibility()
    {
        var overlayUp = _dialogOpen || (Vm?.IsShowingSettings ?? false);
        var video = this.GetVisualDescendants()
            .OfType<LibVLCSharp.Avalonia.VideoView>()
            .FirstOrDefault();
        if (video is not null) video.IsVisible = !overlayUp;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void ApplyPersistedState()
    {
        var s = _windowStateService.Load();
        if (s is null) return;
        Width = s.Width;
        Height = s.Height;
        if (s.X is { } x && s.Y is { } y)
        {
            Position = new Avalonia.PixelPoint(x, y);
        }
        if (s.Maximized) WindowState = Avalonia.Controls.WindowState.Maximized;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Persist size/position before deciding whether this is a real close
        // or a close-to-tray — either way, we want the next launch restored.
        var maximized = WindowState == Avalonia.Controls.WindowState.Maximized;
        var pos = Position;
        var width = maximized ? 1280 : Width;
        var height = maximized ? 800 : Height;
        _windowStateService.Save(new SavedWindowState(width, height, pos.X, pos.Y, maximized));

        if (_allowClose) return;
        if (!ShouldCloseToTray())
        {
            // App.ShutdownMode is OnExplicitShutdown (so tray-close keeps the
            // process alive), which means letting the window close on its own
            // would leave a headless Wipelog.App running. When the user
            // has explicitly turned off close-to-tray, X = quit; route through
            // QuitApp so the lifetime actually shuts down.
            QuitApp();
            return;
        }
        e.Cancel = true;
        Hide();
    }

    private bool ShouldCloseToTray()
    {
        if (App.Current is not App app) return false;
        // Only close-to-tray when the desktop runtime is hosting us — otherwise
        // (designer, test) just behave normally.
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime) return false;
        var prefs = app.Services?.GetService(typeof(AppPreferencesService)) as AppPreferencesService;
        return prefs?.Load().CloseToTray ?? true;
    }

    /// <summary>
    /// Skip the close-to-tray hook on the next <see cref="OnClosing"/>. The
    /// tray "Quit" menu item uses this so an explicit quit actually shuts down.
    /// </summary>
    public void RequestRealClose()
    {
        _allowClose = true;
    }

    private TextBox? FindSearchBox() =>
        this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "SearchBox");

    private DataGrid? FindClipsGrid() =>
        this.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault(t => t.Name == "ClipsGrid");

    private bool ClipListHasFocus()
    {
        var focused = FocusManager?.GetFocusedElement();
        var grid = FindClipsGrid();
        if (grid is null || focused is null) return false;
        if (ReferenceEquals(focused, grid)) return true;
        // Avalonia 11's DataGrid sometimes places its cell/row content
        // presenters into the logical tree without a matching visual-ancestor
        // chain — so fall back to the logical walk when the visual walk
        // doesn't find the grid.
        if (focused is Avalonia.Visual visual
            && visual.GetVisualAncestors().Contains(grid)) return true;
        if (focused is ILogical logical
            && logical.GetLogicalAncestors().Contains(grid)) return true;
        return false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is null) return;

        var focused = FocusManager?.GetFocusedElement();
        // Include numeric/slider/dropdown so their native arrow-key / Home /
        // End / Enter handling isn't hijacked by global player shortcuts when
        // the user is poking at Settings spinners, the volume slider, etc.
        bool inTextInput = focused is TextBox or AutoCompleteBox
            or NumericUpDown or Slider or ComboBox;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        // Always-available shortcuts (work even with text focus).
        switch (e.Key)
        {
            case Key.F when ctrl:
                FindSearchBox()?.Focus();
                e.Handled = true;
                return;
            case Key.L when ctrl:
                FindClipsGrid()?.Focus();
                e.Handled = true;
                return;
            case Key.Q when ctrl:
                QuitApp();
                e.Handled = true;
                return;
            case Key.Escape:
                if (focused is TextBox tb && tb.Name == "SearchBox")
                {
                    tb.Text = string.Empty;
                    e.Handled = true;
                    return;
                }
                // Route focus to the play/pause button so the Tunnel KeyDown
                // handler keeps receiving events (see OnInitialFocus).
                OnInitialFocus(this, e);
                e.Handled = true;
                return;
            case Key.OemQuestion when shift:
                ShowShortcuts();
                e.Handled = true;
                return;
        }

        // List-focused shortcuts: ↑/↓ or J/K navigate, Enter plays. The
        // `return` from each matched case short-circuits the player switch
        // below, so Up/Down don't fall through to AdjustVolume when the
        // table is focused.
        if (ClipListHasFocus() && !inTextInput)
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.J:
                    Vm.ClipList.SelectPrevious();
                    e.Handled = true;
                    return;
                case Key.Down:
                case Key.K:
                    Vm.ClipList.SelectNext();
                    e.Handled = true;
                    return;
                case Key.Enter:
                    if (Vm.ClipList.SelectedItem is not null)
                    {
                        Vm.Player.LoadClip(Vm.ClipList.SelectedItem.Record.VideoPath);
                    }
                    e.Handled = true;
                    return;
            }
        }

        if (inTextInput) return;

        // Alt+1..6: jump to a sidebar entry in the order it appears (M+, Raid,
        // Arena, Solo Shuffle, BG, Clip). Lives above the plain-digit switch
        // so the percentage-seek branch doesn't swallow Alt-modified digits.
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (alt)
        {
            ContentTypeFilter? target = e.Key switch
            {
                Key.D1 or Key.NumPad1 => ContentTypeFilter.MythicPlus,
                Key.D2 or Key.NumPad2 => ContentTypeFilter.Raid,
                Key.D3 or Key.NumPad3 => ContentTypeFilter.Arena,
                Key.D4 or Key.NumPad4 => ContentTypeFilter.SoloShuffle,
                Key.D5 or Key.NumPad5 => ContentTypeFilter.Battleground,
                Key.D6 or Key.NumPad6 => ContentTypeFilter.Clip,
                _ => null,
            };
            if (target is { } t)
            {
                Vm.SideNav.ActiveCategory = t;
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Key.Space:
                Vm.Player.TogglePlayPause();
                e.Handled = true;
                break;
            case Key.J:
                Vm.Player.SeekRelative(System.TimeSpan.FromSeconds(-10));
                e.Handled = true;
                break;
            case Key.L:
                Vm.Player.SeekRelative(System.TimeSpan.FromSeconds(10));
                e.Handled = true;
                break;
            case Key.Left:
                Vm.Player.SeekRelative(System.TimeSpan.FromSeconds(shift ? -30 : -5));
                e.Handled = true;
                break;
            case Key.Right:
                Vm.Player.SeekRelative(System.TimeSpan.FromSeconds(shift ? 30 : 5));
                e.Handled = true;
                break;
            case Key.OemComma:
                Vm.Player.StepFrame(forward: false);
                e.Handled = true;
                break;
            case Key.OemPeriod:
                Vm.Player.StepFrame(forward: true);
                e.Handled = true;
                break;
            case Key.Up:
            case Key.OemPlus:
            case Key.Add:
                Vm.Player.AdjustVolume(+5);
                e.Handled = true;
                break;
            case Key.Down:
            case Key.OemMinus:
            case Key.Subtract:
                Vm.Player.AdjustVolume(-5);
                e.Handled = true;
                break;
            case Key.M:
                Vm.Player.ToggleMute();
                e.Handled = true;
                break;
            case Key.Home:
                Vm.Player.SeekToStart();
                e.Handled = true;
                break;
            case Key.End:
                Vm.Player.SeekToEnd();
                e.Handled = true;
                break;
            case Key.D1:
                Vm.SideNav.ActiveCategory = Wipelog.App.ViewModels.ContentTypeFilter.MythicPlus;
                e.Handled = true;
                break;
            case Key.D2:
                Vm.SideNav.ActiveCategory = Wipelog.App.ViewModels.ContentTypeFilter.Raid;
                e.Handled = true;
                break;
            case Key.D3:
                Vm.SideNav.ActiveCategory = Wipelog.App.ViewModels.ContentTypeFilter.Battleground;
                e.Handled = true;
                break;
            case Key.D4:
                Vm.SideNav.ActiveCategory = Wipelog.App.ViewModels.ContentTypeFilter.Clip;
                e.Handled = true;
                break;
            // 0 and 5-9 stay as player percentage-seek (1-4 are sidebar categories).
            case Key.D0: case Key.D5:
            case Key.D6: case Key.D7: case Key.D8: case Key.D9:
                int digit = e.Key - Key.D0;
                Vm.Player.SeekToFraction(digit / 10.0);
                e.Handled = true;
                break;
            case Key.NumPad0: case Key.NumPad5:
            case Key.NumPad6: case Key.NumPad7: case Key.NumPad8: case Key.NumPad9:
                int npd = e.Key - Key.NumPad0;
                Vm.Player.SeekToFraction(npd / 10.0);
                e.Handled = true;
                break;
            case Key.F:
                _ = Vm.ClipList.ToggleFavoriteSelectedCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Key.Delete:
                _ = Vm.ClipList.DeleteSingleCommand.ExecuteAsync(null);
                e.Handled = true;
                break;
            case Key.T:
                // T cycles Deaths through All → Own → None → All.
                Vm.Player.DisplayOptions.Deaths = Vm.Player.DisplayOptions.Deaths switch
                {
                    Wipelog.App.ViewModels.DeathsFilter.All => Wipelog.App.ViewModels.DeathsFilter.Own,
                    Wipelog.App.ViewModels.DeathsFilter.Own => Wipelog.App.ViewModels.DeathsFilter.None,
                    _ => Wipelog.App.ViewModels.DeathsFilter.All,
                };
                e.Handled = true;
                break;
            case Key.B:
                Vm.Player.DisplayOptions.EncountersVisible = !Vm.Player.DisplayOptions.EncountersVisible;
                e.Handled = true;
                break;
        }
    }

    private void OnInitialFocus(object? sender, EventArgs e)
    {
        // Defer through the dispatcher so Focus() runs after layout — calling
        // it inline at Loaded can be a no-op if the anchor hasn't been arranged
        // yet, which manifests as "hotkeys only start working after I click
        // something."
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => this.FindControl<Button>("KeyboardAnchor")?.Focus(),
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ShowShortcuts()
    {
        if (Vm is null) return;
        Vm.DialogManager.CreateDialog()
            .WithTitle("Keyboard shortcuts")
            .WithContent(new ShortcutsContent())
            .Dismiss().ByClickingBackground()
            .TryShow();
    }

    private void QuitApp()
    {
        _allowClose = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
        // Skip CLR finalizer + LibVLC native-thread teardown — those add a
        // multi-second tail that Ctrl+C avoids by killing the process outright.
        Environment.Exit(0);
    }

    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        if (Vm is null || App.Current is not App app) return;
        if (Vm.Settings is null)
        {
            Vm.Settings = BuildSettingsViewModel(app);
        }
        Vm.IsShowingSettings = true;
    }

    private void OnSceneStubRequested(object? sender, EventArgs e)
    {
        // Scene encoder/recording config has no UI yet — surface a toast-like
        // notice so the sidebar entry isn't a silent no-op. Lives inline
        // because there's nothing to dispatch to elsewhere.
        var win = new Window
        {
            Title = "Scene settings",
            Width = 320, Height = 120,
            CanResize = false,
            SystemDecorations = SystemDecorations.BorderOnly,
            Content = new TextBlock
            {
                Text = "Scene encoder configuration is coming in a later release.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(20),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            },
        };
        _ = win.ShowDialog(this);
    }

    private SettingsViewModel BuildSettingsViewModel(App app)
    {
        var indexer = app.Services.GetRequiredService<ILibraryIndexer>();
        var list = app.Services.GetRequiredService<ClipListViewModel>();
        var player = app.Services.GetRequiredService<PlayerViewModel>();
        var prefs = app.Services.GetRequiredService<AppPreferencesService>();
        var userPrefs = app.Services.GetRequiredService<Wipelog.Core.Preferences.UserPreferencesService>();
        var installer = app.Services.GetRequiredService<IServiceInstaller>();
        var wowLogDetector = app.Services.GetRequiredService<IWowLogDetector>();
        var daemonStatus = app.Services.GetRequiredService<DaemonStatusViewModel>();
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        var db = app.Services.GetRequiredService<ClipDatabase>();
        return new SettingsViewModel(
            indexer, list, player, prefs, userPrefs, installer, wowLogDetector,
            daemonStatus, db.Path, loggerFactory);
    }
}
