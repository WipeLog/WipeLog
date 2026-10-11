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

namespace Wipelog.App.Snapshots;

/// <summary>
/// The real MainWindow at 1280×800. Note: PlayerView's LibVLC VideoView has no
/// native surface headless (FakePlaybackEngine.NativePlayer is null), so the
/// video area is always blank — transport bar / scrub bar / timeline are real.
/// </summary>
public sealed class MainWindowSnapshots(HeadlessSessionFixture session) : IClassFixture<HeadlessSessionFixture>
{
    [Fact]
    public Task Empty_library_daemon_offline() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        var window = await Scenes.MainWindowAsync(world, seed: false);
        Snap.All(window, "main-empty-offline");
    });

    [Fact]
    public Task Library_no_selection_daemon_idle() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus();
        var window = await Scenes.MainWindowAsync(world);
        Snap.All(window, "main-library");
    });

    [Fact]
    public Task Mythic_plus_clip_selected() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus();
        world.EventsLoader.Document = SampleLibrary.Events(1712, "Avanoxx", 2926);
        world.Playback.LengthMs = 1_712_000;
        var window = await Scenes.MainWindowAsync(world);
        await Scenes.SelectClipAsync(world, c => c.Record.KeystoneLevel == 12);
        Snap.All(window, "main-mplus-selected");
    });

    [Fact]
    public Task Raid_wipe_selected_with_deaths() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus();
        world.EventsLoader.Document = SampleLibrary.Events(312);
        world.Playback.LengthMs = 312_000;
        var window = await Scenes.MainWindowAsync(world);
        await Scenes.SelectCategoryAsync(world, ContentTypeFilter.Raid);
        await Scenes.SelectClipAsync(world, c => c.Record.ContentType == ContentType.RaidEncounter && !c.Record.Success);
        Snap.All(window, "main-raid-selected");
    });

    [Fact]
    public Task Daemon_recording() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.RecordingStatus();
        var window = await Scenes.MainWindowAsync(world);
        Snap.All(window, "main-recording");
    });

    [Fact]
    public Task Acl_disabled_and_recorder_error_warnings() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus(
            aclDisabled: true,
            recorderError: "gpu-screen-recorder exited with code 51: display \"DP-3\" not found");
        var window = await Scenes.MainWindowAsync(world);
        Snap.All(window, "main-warnings");
    });

    [Fact]
    public Task Bulk_selection_action_bar() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus();
        var window = await Scenes.MainWindowAsync(world);
        var list = world.Get<ClipListViewModel>();
        foreach (var item in list.Items.Take(3)) list.SelectedItems.Add(item);
        await Snap.WaitAsync();
        Snap.All(window, "main-bulk-selection");
    });
}
