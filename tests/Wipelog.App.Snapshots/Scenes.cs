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
using Avalonia.Layout;
using Avalonia.Media;
using Wipelog.App.ViewModels;
using Wipelog.App.Views;
using Xunit;

namespace Wipelog.App.Snapshots;

/// <summary>Shared setup for snapshot tests.</summary>
internal static class Scenes
{
    public const double Width = 1280;
    public const double Height = 800;

    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The real MainWindow over <paramref name="world"/>'s VM graph at 1280×800.
    /// Does what MainWindowViewModel.InitializeAsync does minus starting the
    /// LibraryWatcher.
    /// NEVER Close() the returned window: with no desktop lifetime,
    /// MainWindow.OnClosing → QuitApp → Environment.Exit(0) kills the test host.
    /// </summary>
    public static async Task<MainWindow> MainWindowAsync(SnapshotWorld world, bool seed = true)
    {
        if (seed) await SampleLibrary.SeedAsync(world, Ct);
        var vm = world.MainWindowVm;
        await vm.ClipList.InitializeAsync();
        await vm.SideNav.RefreshCountsAsync(Ct);
        var window = new MainWindow { DataContext = vm, Width = Width, Height = Height };
        window.Show();
        await Snap.WaitAsync();
        return window;
    }

    /// <summary>Switch the sidebar category and wait for the list to reload.</summary>
    public static async Task SelectCategoryAsync(SnapshotWorld world, ContentTypeFilter filter)
    {
        world.Get<SideNavViewModel>().ActiveCategory = filter;
        await world.Get<ClipListViewModel>().RefreshAsync();
        await Snap.WaitAsync();
    }

    /// <summary>Select a row as a click would (loads the player, details, timeline).</summary>
    public static async Task SelectClipAsync(SnapshotWorld world, Func<ClipListItemViewModel, bool> match)
    {
        var list = world.Get<ClipListViewModel>();
        list.SelectedItem = list.Items.First(match);
        await Snap.WaitAsync();
    }

    /// <summary>
    /// A plain window for a UserControl that normally lives inside a dialog
    /// or flyout. The content keeps its natural size, centred, unless
    /// <paramref name="contentWidth"/> pins it.
    /// </summary>
    public static Window Host(Control content, double? contentWidth = null)
    {
        content.HorizontalAlignment = contentWidth is null ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        content.VerticalAlignment = contentWidth is null ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        if (contentWidth is { } w) content.Width = w;
        var window = new Window
        {
            Width = Width,
            Height = Height,
            Content = content,
        };
        // Same surface brush the app's own dialogs use.
        if (window.TryFindResource("BackgroundSurfaceBrush", window.ActualThemeVariant, out var brush))
            window.Background = brush as IBrush;
        window.Show();
        return window;
    }
}
