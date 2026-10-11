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

using Avalonia.VisualTree;
using SukiUI.Controls.GlassMorphism;
using SukiUI.Dialogs;
using Wipelog.App.ViewModels;
using Wipelog.App.Views;
using Wipelog.Core.Detection;
using Xunit;

namespace Wipelog.App.Snapshots;

/// <summary>
/// Dialog contents shown through SukiUI's dialog host over MainWindow, built
/// with the same CreateDialog() chains the app uses (MainWindow.ShowShortcuts,
/// ClipsTableView.OnContextEditMetadata).
///
/// Headless limitation: SukiUI's glass card paints a SukiBlurBackground via a
/// custom Skia "blur behind" operation that can't run on the headless CPU
/// surface — left in, it turns the WHOLE captured frame black. The tests hide
/// those controls before capturing, so the dialog card renders without its
/// frosted-glass backdrop (flat, slightly more opaque than on a real desktop).
/// </summary>
public sealed class OverlaySnapshots(HeadlessSessionFixture session) : IClassFixture<HeadlessSessionFixture>
{
    private static void HideBlurBackdrops(Avalonia.Visual root)
    {
        foreach (var blur in root.GetVisualDescendants().OfType<SukiBlurBackground>())
            blur.IsVisible = false;
    }

    [Fact]
    public Task Keyboard_shortcuts_dialog() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus();
        var window = await Scenes.MainWindowAsync(world);
        world.MainWindowVm.DialogManager.CreateDialog()
            .WithTitle("Keyboard shortcuts")
            .WithContent(new ShortcutsContent())
            .Dismiss().ByClickingBackground()
            .TryShow();
        await Snap.WaitAsync();
        HideBlurBackdrops(window);
        Snap.All(window, "dialog-shortcuts");
    });

    [Fact]
    public Task Edit_clip_metadata_dialog() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus();
        var window = await Scenes.MainWindowAsync(world);
        var record = world.Get<ClipListViewModel>().Items.First(c => c.Record.ContentType == ContentType.MythicPlus).Record;
        world.MainWindowVm.DialogManager.CreateDialog()
            .WithTitle("Edit clip metadata")
            .WithContent(new EditClipMetadataContent { DataContext = new EditClipMetadataDialogViewModel(record) })
            .WithActionButton("Save", _ => { }, dismissOnClick: true)
            .WithActionButton("Cancel", _ => { }, dismissOnClick: true)
            .TryShow();
        await Snap.WaitAsync();
        HideBlurBackdrops(window);
        Snap.All(window, "dialog-edit-metadata");
    });
}
