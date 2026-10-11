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

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using Wipelog.App.Services;
using Wipelog.App.ViewModels;
using Wipelog.Core.Preferences;
using Wipelog.Core.Status;
using Wipelog.Core.Time;
using Wipelog.Library;

namespace Wipelog.App.Snapshots;

/// <summary>
/// The App's real view-model graph (same registrations as
/// <c>AppServices.Build</c>) wired to a throwaway world: a temp SQLite
/// library, temp preferences, and fakes for everything that would touch the
/// live machine (libvlc, systemd, daemon heartbeat/socket, WoW install scan).
/// One per test; dispose deletes the temp dir.
/// </summary>
public sealed class SnapshotWorld : IDisposable
{
    /// <summary>
    /// "Now" for the sample data and the fake clock. Anchored to the real wall
    /// clock (truncated to the minute) because ClipListItemViewModel's "2h ago"
    /// labels and ClipFiltersViewModel's "Last 30 days" window read
    /// DateTimeOffset.Now directly — relative output stays stable run to run.
    /// </summary>
    public static readonly DateTimeOffset Now = TruncateToMinute(DateTimeOffset.Now);

    private static DateTimeOffset TruncateToMinute(DateTimeOffset t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);

    // Display paths for Settings. They don't exist on purpose: the
    // gpu-screen-recorder path makes Settings' monitor / app-audio probes fail
    // fast instead of shelling out to whatever the host has installed.
    public const string FakeHome = "/home/raider";

    private readonly ServiceProvider _services;

    public string Root { get; }
    public string ClipsDirectory { get; }
    public FakePlaybackEngine Playback { get; } = new();
    public FakeDaemonStatusReader DaemonStatusReader { get; } = new();
    public FakeServiceInstaller Installer { get; }
    public FakeWowLogDetector WowLogDetector { get; } = new();
    public FakeClipEventsLoader EventsLoader { get; } = new();
    public FixedClock Clock { get; } = new(Now);
    public UserPreferencesService UserPreferences { get; }
    public ClipDatabase Database { get; }

    public SnapshotWorld()
    {
        Root = Path.Combine(TestEnvironment.Root, "world-" + Guid.NewGuid().ToString("N")[..8]);
        ClipsDirectory = Path.Combine(Root, "clips");
        Directory.CreateDirectory(ClipsDirectory);
        Installer = new FakeServiceInstaller(Path.Combine(Root, "wipelog.service"));
        UserPreferences = new UserPreferencesService(Path.Combine(Root, "preferences.json"));
        Database = new ClipDatabase(Path.Combine(Root, "clips.db"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClipDatabase>.Instance);

        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton(Database);
        s.AddSingleton<IClipRepository, ClipRepository>();
        s.AddSingleton<ILibraryIndexer, LibraryIndexer>();
        s.AddSingleton<IPlaybackEngine>(Playback);
        s.AddSingleton<ISukiDialogManager>(_ => new SukiDialogManager());
        s.AddSingleton<ISukiToastManager>(_ => new SukiToastManager());
        s.AddSingleton<IDialogService, FakeDialogService>();
        s.AddSingleton<IClipEventsLoader>(EventsLoader);
        s.AddSingleton<IClipArchiver, FakeClipArchiver>();
        s.AddSingleton<LibraryWatcher>();          // constructed, never started
        s.AddSingleton<TimelinePreferencesService>();   // XDG_CONFIG_HOME → temp (TestEnvironment)
        s.AddSingleton(UserPreferences);
        s.AddSingleton(_ => new AppPreferencesService(UserPreferences));
        s.AddSingleton<IServiceInstaller>(Installer);
        s.AddSingleton<IClock>(Clock);
        s.AddSingleton<IDaemonStatusReader>(DaemonStatusReader);
        s.AddSingleton<IDaemonLifecycleController, FakeDaemonLifecycleController>();
        s.AddSingleton<IDaemonControlClient, FakeDaemonControlClient>();
        s.AddSingleton<IWowLogDetector>(WowLogDetector);
        s.AddSingleton<DaemonStatusViewModel>();
        s.AddSingleton<ClipFiltersViewModel>();
        s.AddSingleton<ClipListViewModel>();
        s.AddSingleton<TagsEditorViewModel>();
        s.AddSingleton<DisplayOptionsViewModel>();
        s.AddSingleton<PlayerViewModel>();
        s.AddSingleton<TimelineViewModel>();
        s.AddSingleton<SideNavViewModel>();
        s.AddSingleton<FilterBarViewModel>();
        s.AddSingleton<ClipDetailsViewModel>();
        s.AddSingleton<MainWindowViewModel>();
        _services = s.BuildServiceProvider();
        SnapshotApp.AttachServices(_services);
    }

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public IClipRepository Repository => Get<IClipRepository>();
    public MainWindowViewModel MainWindowVm => Get<MainWindowViewModel>();

    /// <summary>Realistic configured preferences (combat-log dir set → no onboarding banner).</summary>
    public void UseConfiguredPreferences(bool logDirectorySet = true)
    {
        UserPreferences.Save(new UserPreferences
        {
            Daemon = new DaemonConfig
            {
                DaemonExecutablePath = FakeHome + "/bin/wipelog/Wipelog.Daemon",
                LogDirectory = logDirectorySet
                    ? FakeHome + "/Games/battlenet/drive_c/Program Files (x86)/World of Warcraft/_retail_/Logs"
                    : "",
                OutputDirectory = FakeHome + "/Videos/wow",
                DatabasePath = FakeHome + "/.local/share/wipelog/clips.db",
                GpuScreenRecorderExecutable = FakeHome + "/.local/bin/gpu-screen-recorder",
                CaptureTarget = "DP-1",
                Framerate = 60,
                Codec = "h264",
            },
        });
    }

    /// <summary>Same construction MainWindow.BuildSettingsViewModel does, minus App.Services.</summary>
    public SettingsViewModel CreateSettingsViewModel() => new(
        Get<ILibraryIndexer>(), Get<ClipListViewModel>(), Get<PlayerViewModel>(),
        Get<AppPreferencesService>(), UserPreferences, Installer, WowLogDetector,
        Get<DaemonStatusViewModel>(), Database.Path, Get<ILoggerFactory>());

    public static DaemonStatus IdleStatus(bool aclDisabled = false, string? recorderError = null) =>
        new("0.0.1", 4242, Now.AddHours(-3), Now, DaemonState.Idle, null, 7, null)
        {
            AdvancedLoggingDisabled = aclDisabled,
            RecorderError = recorderError,
            DiskUsage = new DiskUsageInfo(
                UsedBytes: 412L << 30, TotalBytes: 931L << 30, FreeBytes: 519L << 30,
                MinFreeBytes: 20L << 30, UsagePercent: 44.3),
        };

    public static DaemonStatus RecordingStatus() =>
        IdleStatus() with
        {
            State = DaemonState.Recording,
            CurrentRecording = new Wipelog.Core.Status.RecordingStatus(
                "MythicPlus", "Ara-Kara, City of Echoes", Now.AddMinutes(-14).AddSeconds(-27), 12),
        };

    public void Dispose()
    {
        _services.Dispose();
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Redirects every XDG base dir to a per-run temp folder before any test
/// code runs, so services that compute their own paths
/// (TimelinePreferencesService, WindowStateService inside MainWindow, ...)
/// never read or write the real ~/.config / ~/.local/share.
/// </summary>
internal static class TestEnvironment
{
    public static string Root { get; } =
        Path.Combine(Path.GetTempPath(), "wipelog-snapshots-" + Environment.ProcessId);

    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Root);
        foreach (var v in new[] { "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR" })
        {
            var dir = Path.Combine(Root, v.ToLowerInvariant());
            Directory.CreateDirectory(dir);
            Environment.SetEnvironmentVariable(v, dir);
        }
    }
}
