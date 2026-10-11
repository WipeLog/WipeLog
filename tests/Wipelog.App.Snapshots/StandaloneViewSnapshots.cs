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
using Wipelog.App.Views;
using Wipelog.Core.Detection;
using Xunit;

namespace Wipelog.App.Snapshots;

/// <summary>
/// Views that can't be reached in a MainWindow frame capture: the date-range
/// flyout (headless popups are separate top-levels, not part of the main
/// window's frame) and the per-type rows of ClipDetailsView not covered by
/// the MainWindow tests. Hosted in a 1280×800 window.
/// </summary>
public sealed class StandaloneViewSnapshots(HeadlessSessionFixture session) : IClassFixture<HeadlessSessionFixture>
{
    [Fact]
    public Task Date_range_flyout_custom_range() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        var filters = world.Get<ClipFiltersViewModel>();
        filters.CustomStart = SnapshotWorld.Now.Date.AddDays(-14);
        filters.CustomEnd = SnapshotWorld.Now.Date.AddDays(-7);
        var window = Scenes.Host(new DateRangeFlyoutView { DataContext = filters });
        await Snap.WaitAsync();
        Snap.All(window, "flyout-date-range");
    });

    [Theory]
    [InlineData(ContentTypeFilter.Arena, ContentType.Arena)]
    [InlineData(ContentTypeFilter.SoloShuffle, ContentType.SoloShuffle)]
    [InlineData(ContentTypeFilter.Battleground, ContentType.Battleground)]
    [InlineData(ContentTypeFilter.Clip, ContentType.Manual)]
    public Task Clip_details_panel(ContentTypeFilter category, ContentType type) => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.EventsLoader.Document = null;   // PvP/manual clips: no comp sidecar
        // Resolve before selecting: it follows ClipListViewModel.SelectedItem changes.
        var details = world.Get<ClipDetailsViewModel>();
        await SampleLibrary.SeedAsync(world, Scenes.Ct);
        await Scenes.SelectCategoryAsync(world, category);
        await Scenes.SelectClipAsync(world, c => c.Record.ContentType == type);
        // MainWindow gives this view a 300px column; keep that width.
        var window = Scenes.Host(new ClipDetailsView { DataContext = details }, contentWidth: 300);
        await Snap.WaitAsync();
        Snap.All(window, "details-" + type.ToString().ToLowerInvariant());
    });
}
