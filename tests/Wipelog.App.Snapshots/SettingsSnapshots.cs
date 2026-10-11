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

using Wipelog.App.Services;
using Wipelog.App.ViewModels;
using Xunit;

namespace Wipelog.App.Snapshots;

/// <summary>
/// SettingsView as the app shows it: full-page inside MainWindow
/// (IsShowingSettings), one test per tab plus the onboarding and
/// pending-reinstall states.
/// </summary>
public sealed class SettingsSnapshots(HeadlessSessionFixture session) : IClassFixture<HeadlessSessionFixture>
{
    private static async Task<(Views.MainWindow Window, SettingsViewModel Settings)> OpenAsync(
        SnapshotWorld world, SettingsTab tab)
    {
        world.DaemonStatusReader.Current = SnapshotWorld.IdleStatus();
        var window = await Scenes.MainWindowAsync(world);
        var vm = world.MainWindowVm;
        vm.Settings = world.CreateSettingsViewModel();
        vm.IsShowingSettings = true;
        vm.Settings.ActiveTab = tab;
        await Snap.WaitAsync();
        return (window, vm.Settings);
    }

    [Theory]
    [InlineData(SettingsTab.General)]
    [InlineData(SettingsTab.Recording)]
    [InlineData(SettingsTab.Storage)]
    [InlineData(SettingsTab.Service)]
    public Task Tab(SettingsTab tab) => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.UseConfiguredPreferences();
        var (window, _) = await OpenAsync(world, tab);
        Snap.All(window, "settings-" + tab.ToString().ToLowerInvariant());
    });

    [Fact]
    public Task Onboarding_banner_with_detected_logs() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.UseConfiguredPreferences(logDirectorySet: false);
        world.Installer.State = ServiceInstallState.NotInstalled;
        world.WowLogDetector.Results.Add(new DetectedLogPath(
            SnapshotWorld.FakeHome + "/Games/battlenet/drive_c/Program Files (x86)/World of Warcraft/_retail_/Logs",
            14, SnapshotWorld.Now.AddHours(-2)));
        world.WowLogDetector.Results.Add(new DetectedLogPath(
            SnapshotWorld.FakeHome + "/.wine/drive_c/Program Files (x86)/World of Warcraft/_retail_/Logs",
            3, SnapshotWorld.Now.AddDays(-200)));
        var (window, settings) = await OpenAsync(world, SettingsTab.General);
        settings.DetectWowLogsCommand.Execute(null);
        await Snap.WaitAsync();
        Snap.All(window, "settings-onboarding");
    });

    // SystemdUnitFileBuilder is internal to the App; the installed unit must
    // be byte-identical to its output for HasPendingUnitChanges to start false.
    private static string BuildUnitFile(Wipelog.Core.Preferences.DaemonConfig config) =>
        (string)typeof(IServiceInstaller).Assembly
            .GetType("Wipelog.App.Services.SystemdUnitFileBuilder", throwOnError: true)!
            .GetMethod("Build")!
            .Invoke(null, [config])!;

    [Fact]
    public Task Service_tab_pending_reinstall() => session.Run(async () =>
    {
        using var world = new SnapshotWorld();
        world.UseConfiguredPreferences();
        var config = world.UserPreferences.Load().Daemon;
        await File.WriteAllTextAsync(world.Installer.ServiceFilePath, BuildUnitFile(config), Scenes.Ct);
        var (window, settings) = await OpenAsync(world, SettingsTab.Service);
        settings.Framerate = 30;   // a unit-baked setting → amber "reinstall" hint
        await Snap.WaitAsync();
        Snap.All(window, "settings-service-pending");
    });
}
