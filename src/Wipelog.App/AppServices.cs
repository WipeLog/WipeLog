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

using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using Wipelog.App.Services;
using Wipelog.App.ViewModels;
using Wipelog.Core.Preferences;
using Wipelog.Core.Time;
using Wipelog.Library;

namespace Wipelog.App;

internal static class AppServices
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        services.AddLogging(b => b.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        }));

        var dbPath = ClipDatabase.DefaultPath();
        services.AddSingleton(sp => new ClipDatabase(
            dbPath,
            sp.GetRequiredService<ILogger<ClipDatabase>>()));
        services.AddSingleton<IClipRepository, ClipRepository>();
        services.AddSingleton<ILibraryIndexer, LibraryIndexer>();

        services.AddSingleton<LibVLC>(_ => new LibVLC(
            "--no-video-title-show",
            "--quiet",
            "--no-osd",
            "--no-snapshot-preview",
            "--vout=none"));
        services.AddSingleton<IPlaybackEngine, VlcPlaybackEngine>();
        // SukiUI dialog + toast managers. One instance each, bound to the
        // hosts in SukiWindow and injected into DialogService / VMs.
        services.AddSingleton<ISukiDialogManager>(_ => new SukiDialogManager());
        services.AddSingleton<ISukiToastManager>(_ => new SukiToastManager());
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IClipEventsLoader, ClipEventsLoader>();
        services.AddSingleton<IClipArchiver, ClipArchiver>();
        services.AddSingleton<LibraryWatcher>();
        services.AddSingleton<WindowStateService>();
        services.AddSingleton<TimelinePreferencesService>();
        services.AddSingleton<AppPreferencesService>();
        services.AddSingleton<UserPreferencesService>();
        services.AddSingleton<IServiceInstaller>(sp => new SystemdServiceInstaller(
            sp.GetRequiredService<ILogger<SystemdServiceInstaller>>()));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDaemonStatusReader>(sp => new FileBasedDaemonStatusReader(
            FileBasedDaemonStatusReader.DefaultStatusPath(),
            sp.GetRequiredService<IClock>(),
            sp.GetRequiredService<ILogger<FileBasedDaemonStatusReader>>()));
        services.AddSingleton<IDaemonLifecycleController>(sp =>
            new SystemctlDaemonLifecycleController(
                sp.GetRequiredService<ILogger<SystemctlDaemonLifecycleController>>()));
        services.AddSingleton<IDaemonControlClient>(sp =>
            new DaemonControlClient(sp.GetRequiredService<ILogger<DaemonControlClient>>()));
        services.AddSingleton<IWowLogDetector>(_ => new WowLogDetector());
        services.AddSingleton<DaemonStatusViewModel>();
        services.AddSingleton<ClipFiltersViewModel>();
        services.AddSingleton<ClipListViewModel>();
        services.AddSingleton<TagsEditorViewModel>();
        services.AddSingleton<DisplayOptionsViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<TimelineViewModel>();
        services.AddSingleton<SideNavViewModel>();
        services.AddSingleton<FilterBarViewModel>();
        services.AddSingleton<ClipDetailsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }
}
