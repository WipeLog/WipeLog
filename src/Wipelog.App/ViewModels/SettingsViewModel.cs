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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wipelog.App.Services;
using Wipelog.Core.Detection;
using Wipelog.Core.Preferences;
using Wipelog.Core.Recording;
using Wipelog.Core.Storage;
using Wipelog.Library;

namespace Wipelog.App.ViewModels;

public enum SettingsTab
{
    General,
    Recording,
    Storage,
    Service,
}

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ILibraryIndexer _indexer;
    private readonly ClipListViewModel _list;
    private readonly PlayerViewModel _player;
    private readonly AppPreferencesService _prefsService;
    private readonly UserPreferencesService _userPrefsService;
    private readonly IServiceInstaller _installer;
    private readonly IWowLogDetector _wowLogDetector;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SettingsViewModel> _logger;

    public string DatabasePath { get; }
    public DaemonStatusViewModel DaemonStatus { get; }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private SettingsTab _activeTab = SettingsTab.General;

    public bool IsGeneralTab => ActiveTab == SettingsTab.General;
    public bool IsRecordingTab => ActiveTab == SettingsTab.Recording;
    public bool IsStorageTab => ActiveTab == SettingsTab.Storage;
    public bool IsServiceTab => ActiveTab == SettingsTab.Service;

    partial void OnActiveTabChanged(SettingsTab value)
    {
        OnPropertyChanged(nameof(IsGeneralTab));
        OnPropertyChanged(nameof(IsRecordingTab));
        OnPropertyChanged(nameof(IsStorageTab));
        OnPropertyChanged(nameof(IsServiceTab));
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        ActiveTab = tab switch
        {
            "Recording" => SettingsTab.Recording,
            "Storage" => SettingsTab.Storage,
            "Service" => SettingsTab.Service,
            _ => SettingsTab.General,
        };
    }

    // Timeline prefs (unchanged)
    [ObservableProperty]
    private int _timelineSeekBackSeconds;

    [ObservableProperty]
    private bool _timelinePauseOnClick;

    // Notification + tray prefs
    [ObservableProperty]
    private bool _notifyOnClipSaved;

    [ObservableProperty]
    private bool _notifyOnErrors;

    [ObservableProperty]
    private bool _closeToTray;

    // Daemon paths
    [ObservableProperty]
    private string _daemonExecutablePath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnboardingNeeded))]
    private string _logDirectory = "";

    [ObservableProperty]
    private string _logPattern = "";

    [ObservableProperty]
    private string _outputDirectory = "";

    [ObservableProperty]
    private string _daemonDatabasePath = "";

    // Encoder + capture
    [ObservableProperty]
    private string _gpuScreenRecorderExecutable = "";

    [ObservableProperty]
    private string _captureTarget = "";

    [ObservableProperty]
    private int _framerate;

    [ObservableProperty]
    private string _codec = "";

    [ObservableProperty]
    private string _container = "";

    // One row per audio track in the recording. Each track becomes a separate
    // `-a` flag → a separate mp4 audio stream, so the user can isolate game /
    // Spotify / Discord / misc in their editor of choice. Order preserved.
    public ObservableCollection<AudioTrackEntry> AudioTracks { get; } = new();

    // Suggestions surfaced under the "Add track" flyout: built-in gsr options
    // plus any currently-running PipeWire applications discovered via
    // `gpu-screen-recorder --list-application-audio`. Refreshed lazily.
    public ObservableCollection<AudioSourceSuggestion> AudioSourceSuggestions { get; } = new();

    [ObservableProperty]
    private string _videoQuality = "";

    // Output resolution. "source" = no scaling (record at the capture monitor's
    // native size); "720p"/"1080p"/"1440p" downscale during encode. Width is
    // derived at recording time from the actual monitor's aspect ratio so
    // ultrawides stay un-letterboxed. Lives in the systemd unit (reinstall to
    // apply).
    [ObservableProperty]
    private string _videoResolution = VideoResolutions.Source;

    // Pre-encounter buffer ("pre-roll"). When PreRollEnabled is true the
    // daemon launches a second gpu-screen-recorder in replay-buffer mode
    // and stitches its output onto the front of each clip. PreRollSeconds
    // is the buffer length; we keep the value through enable/disable cycles
    // so users don't have to retype it.
    [ObservableProperty]
    private bool _preRollEnabled;

    [ObservableProperty]
    private int _preRollSeconds = 30;

    public int PreRollSecondsMin => 2;
    public int PreRollSecondsMax => 300;

    // Post-encounter tail ("post-roll"). When PostRollEnabled is true the
    // RecorderController holds each clip open for PostRollSeconds extra
    // seconds after the encounter's stop signal. No second process — just a
    // Task.Delay before recorder.StopAsync. We keep the value through
    // enable/disable cycles so users don't have to retype it.
    [ObservableProperty]
    private bool _postRollEnabled;

    [ObservableProperty]
    private int _postRollSeconds = 5;

    public int PostRollSecondsMin => 1;
    public int PostRollSecondsMax => 60;

    // Chat overlay mask. When ChatOverlayEnabled is true the daemon composites
    // a black rectangle over the configured screen region of each saved clip
    // (an ffmpeg post-process). Read at daemon startup, so changes take effect
    // on the next service reinstall/restart. Not part of the systemd unit, so it
    // never trips the pending-unit-changes banner.
    [ObservableProperty]
    private bool _chatOverlayEnabled;

    [ObservableProperty]
    private int _chatOverlayX;

    // Y is measured from the bottom edge (WoW chat sits bottom-left), so 0 == flush bottom.
    [ObservableProperty]
    private int _chatOverlayY;

    [ObservableProperty]
    private int _chatOverlayWidth = 540;

    [ObservableProperty]
    private int _chatOverlayHeight = 320;

    [ObservableProperty]
    private string _testMaskMessage = string.Empty;

    public int ChatOverlayPosMin => 0;    // X / Y
    public int ChatOverlaySizeMin => 1;   // W / H — a zero-sized box is invisible
    public int ChatOverlayMax => 7680;

    // CONTENT filters. The daemon reads these from preferences.json once at
    // startup, so changes take effect on the next service reinstall/restart.
    // Defaults record everything; users opt into being more restrictive.
    [ObservableProperty]
    private bool _recordRaids = true;

    [ObservableProperty]
    private bool _recordMythicPlus = true;

    [ObservableProperty]
    private bool _recordBattlegrounds = true;

    [ObservableProperty]
    private int _minKeystoneLevel = 2;

    public int MinKeystoneLevelMin => 2;
    public int MinKeystoneLevelMax => 30;

    // Free-space floor (GB) the daemon maintains on the clips drive. Editable
    // even when the daemon is offline; the live whole-disk readout comes from
    // the heartbeat via DaemonStatus. Changes apply on the next service
    // reinstall/restart.
    [ObservableProperty]
    private long _minFreeGigabytes = 50;

    public long MinFreeGigabytesMin => 5;

    // Upper bound is the size of the drive holding the clips — you can't keep
    // more free than the disk holds. Falls back to 2000 GB when the drive size
    // can't be determined. Recomputed when the output directory changes.
    [ObservableProperty]
    private long _minFreeGigabytesMax = 2000;

    /// <summary>
    /// Largest free-space floor (GB) that makes sense for the drive behind the
    /// clip output directory: the drive's total size, never below
    /// <paramref name="floorGb"/>. 2000 GB fallback when the drive is unknown.
    /// </summary>
    internal static long MaxFreeGigabytesForDisk(Wipelog.Core.Storage.DiskSpaceInfo? disk, long floorGb)
    {
        if (disk is null || disk.TotalBytes <= 0) return 2000;
        long totalGb = disk.TotalBytes / (1024L * 1024 * 1024);
        return Math.Max(floorGb, totalGb);
    }

    // Two collections wired to the difficulty ChipSelector: Selected holds the
    // recorded tiers (chips); Available holds the rest (the Add flyout). The
    // control moves items between them; we persist on Selected changing.
    public ObservableCollection<RaidDifficulty> SelectedRaidDifficulties { get; } = new();

    public ObservableCollection<RaidDifficulty> AvailableRaidDifficulties { get; } = new();

    // Canonical tier order for laying chips out predictably.
    public static IReadOnlyList<RaidDifficulty> AllRaidDifficulties { get; } = new[]
    {
        RaidDifficulty.LFR, RaidDifficulty.Normal, RaidDifficulty.Heroic, RaidDifficulty.Mythic,
    };

    // First-run onboarding: the banner at the top of Settings hides itself
    // once the user has at least the combat-log directory set. Output dir +
    // daemon exec path have safe defaults so they don't gate the banner.
    public bool IsOnboardingNeeded => string.IsNullOrWhiteSpace(LogDirectory);

    /// <summary>
    /// Candidate combat-log directories surfaced by the "Detect WoW logs"
    /// button. Empty until the user clicks Detect; cleared when they pick
    /// one. <see cref="HasDetectedLogPaths"/> gates the picker UI.
    /// </summary>
    public ObservableCollection<DetectedLogPath> DetectedLogPaths { get; } = new();

    public bool HasDetectedLogPaths => DetectedLogPaths.Count > 0;

    [ObservableProperty]
    private bool _isDetectingWowLogs;

    /// <summary>"Found 2 candidates", "No WoW logs detected" - shown under
    /// the Detect button. Empty until the user clicks Detect.</summary>
    [ObservableProperty]
    private string _detectionMessage = string.Empty;

    // Service state
    [ObservableProperty]
    private ServiceInstallState _serviceState = ServiceInstallState.NotInstalled;

    // True when the installed systemd unit no longer matches the current
    // settings — i.e. a daemon-arg setting (quality, audio, framerate, codec,
    // pre-roll, paths) changed and needs a reinstall to take effect. Recording
    // filters are excluded on purpose: they hot-reload from preferences.json.
    [ObservableProperty]
    private bool _hasPendingUnitChanges;

    [ObservableProperty]
    private string _serviceMessage = string.Empty;

    [ObservableProperty]
    private bool _isServiceWorking;

    // Test-recording state. The button under the RECORDING form spawns a
    // short gpu-screen-recorder run so users can verify their capture +
    // audio + codec settings without waiting for a real pull.
    [ObservableProperty]
    private int _testRecordingSeconds = 5;

    [ObservableProperty]
    private bool _isTestRecording;

    [ObservableProperty]
    private string _testRecordingMessage = string.Empty;

    private CancellationTokenSource? _testRecordingCts;

    public int TimelineSeekBackMin => TimelinePreferences.MinSeekBackSeconds;
    public int TimelineSeekBackMax => TimelinePreferences.MaxSeekBackSeconds;

    public int TestRecordingSecondsMin => 3;
    public int TestRecordingSecondsMax => 15;

    public IReadOnlyList<string> VideoQualityChoices { get; } =
        new[] { "medium", "high", "very_high", "ultra" };

    public IReadOnlyList<string> VideoResolutionChoices { get; } = VideoResolutions.UserChoices;

    public IReadOnlyList<string> CodecChoices { get; } =
        new[] { "h264", "hevc", "av1" };

    public IReadOnlyList<string> ContainerChoices { get; } =
        new[] { "mp4", "mkv" };

    // Built-in gpu-screen-recorder capture modes + any monitors detected at
    // runtime via `gpu-screen-recorder --list-monitors`. Monitor names get
    // appended asynchronously after the viewmodel is constructed; the
    // ObservableCollection keeps the bound ComboBox in sync.
    public ObservableCollection<string> CaptureTargetChoices { get; } =
        new(new[] { "focused", "portal", "region" });

    public string ServiceFilePath => _installer.ServiceFilePath;

    // For binding visibility states of buttons.
    public bool IsNotInstalled => ServiceState == ServiceInstallState.NotInstalled;
    public bool IsInstalled => ServiceState != ServiceInstallState.NotInstalled;

    // The amber pending-changes banner has its own "Reinstall now to apply"
    // button, so the action-row reinstall is only shown as a manual fallback
    // when there are no pending changes (avoids duplicate buttons).
    public bool ShowReinstallButton => IsInstalled && !HasPendingUnitChanges;

    private bool _suppressPersist;

    public SettingsViewModel(
        ILibraryIndexer indexer,
        ClipListViewModel list,
        PlayerViewModel player,
        AppPreferencesService prefsService,
        UserPreferencesService userPrefsService,
        IServiceInstaller installer,
        IWowLogDetector wowLogDetector,
        DaemonStatusViewModel daemonStatus,
        string databasePath,
        ILoggerFactory loggerFactory)
    {
        _indexer = indexer;
        _list = list;
        _player = player;
        _prefsService = prefsService;
        _userPrefsService = userPrefsService;
        _installer = installer;
        _wowLogDetector = wowLogDetector;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<SettingsViewModel>();
        DaemonStatus = daemonStatus;
        DatabasePath = databasePath;

        var tprefs = _player.TimelinePreferences;
        _timelineSeekBackSeconds = tprefs.SeekBackSeconds;
        _timelinePauseOnClick = tprefs.PauseOnClick;

        var prefs = _userPrefsService.Load();
        // Installed unit file wins over preferences.json for daemon config —
        // it's what the running service actually uses, so a stale or wrong
        // prefs file shouldn't silently overwrite live config on the next
        // install/reinstall.
        var daemonConfig = LoadInstalledDaemonConfig() ?? prefs.Daemon;
        _suppressPersist = true;
        try
        {
            _notifyOnClipSaved = prefs.NotifyOnClipSaved;
            _notifyOnErrors = prefs.NotifyOnErrors;
            _closeToTray = prefs.CloseToTray;
            _testRecordingSeconds = Math.Clamp(
                prefs.TestRecordingSeconds, TestRecordingSecondsMin, TestRecordingSecondsMax);
            ApplyDaemonConfig(daemonConfig);
            ApplyRecordingFilters(prefs.RecordingFilters);
            RecomputeStorageMax();
            _minFreeGigabytes = Math.Clamp(
                prefs.StorageManagement.MinFreeGigabytes, MinFreeGigabytesMin, MinFreeGigabytesMax);
            _chatOverlayEnabled = prefs.ChatOverlay.Enabled;
            _chatOverlayX = Math.Clamp(prefs.ChatOverlay.X, ChatOverlayPosMin, ChatOverlayMax);
            _chatOverlayY = Math.Clamp(prefs.ChatOverlay.Y, ChatOverlayPosMin, ChatOverlayMax);
            _chatOverlayWidth = Math.Clamp(prefs.ChatOverlay.Width, ChatOverlaySizeMin, ChatOverlayMax);
            _chatOverlayHeight = Math.Clamp(prefs.ChatOverlay.Height, ChatOverlaySizeMin, ChatOverlayMax);
        }
        finally
        {
            _suppressPersist = false;
        }
        AudioTracks.CollectionChanged += OnAudioTracksCollectionChanged;
        SelectedRaidDifficulties.CollectionChanged += OnSelectedRaidDifficultiesChanged;

        _ = RefreshServiceStateAsync();
        _ = LoadMonitorsAsync();
        _ = LoadAudioSuggestionsAsync();
    }

    private void OnAudioTracksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (AudioTrackEntry entry in e.NewItems)
            {
                entry.PropertyChanged += OnAudioTrackEntryChanged;
            }
        }
        if (e.OldItems is not null)
        {
            foreach (AudioTrackEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= OnAudioTrackEntryChanged;
            }
        }
        PersistUserPrefs();
    }

    private void OnAudioTrackEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        // DerivedLabel is computed off Source and fires together — only persist
        // on the source change to avoid double-writes.
        if (e.PropertyName == nameof(AudioTrackEntry.Source))
        {
            PersistUserPrefs();
        }
    }

    private async Task LoadMonitorsAsync()
    {
        try
        {
            var exe = string.IsNullOrWhiteSpace(GpuScreenRecorderExecutable)
                ? "gpu-screen-recorder"
                : GpuScreenRecorderExecutable;
            var monitors = await GpuScreenRecorderMonitorList
                .QueryAsync(exe, CancellationToken.None)
                .ConfigureAwait(false);
            if (monitors.Count == 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var m in monitors)
                {
                    if (!CaptureTargetChoices.Contains(m.Name))
                    {
                        CaptureTargetChoices.Add(m.Name);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Could not enumerate monitors for Capture target dropdown — built-in modes only.");
        }
    }

    private DaemonConfig? LoadInstalledDaemonConfig()
    {
        try
        {
            var path = _installer.ServiceFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            var content = File.ReadAllText(path);
            return SystemdUnitFileParser.Parse(content);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse installed unit file; falling back to preferences.json.");
            return null;
        }
    }

    private void ApplyDaemonConfig(DaemonConfig d)
    {
        DaemonExecutablePath = d.DaemonExecutablePath;
        LogDirectory = d.LogDirectory;
        LogPattern = d.LogPattern;
        OutputDirectory = d.OutputDirectory;
        DaemonDatabasePath = d.DatabasePath;
        GpuScreenRecorderExecutable = d.GpuScreenRecorderExecutable;
        // Seed the choice list with the configured target before binding fires.
        // Otherwise the ComboBox can't match a monitor name (added async by
        // LoadMonitorsAsync) and clears SelectedItem back through the TwoWay
        // binding, overwriting the persisted value with empty.
        if (!string.IsNullOrWhiteSpace(d.CaptureTarget) &&
            !CaptureTargetChoices.Contains(d.CaptureTarget))
        {
            CaptureTargetChoices.Add(d.CaptureTarget);
        }
        CaptureTarget = d.CaptureTarget;
        Framerate = d.Framerate;
        Codec = d.Codec;
        Container = d.Container;
        VideoQuality = d.VideoQuality;
        VideoResolution = VideoResolutions.IsValid(d.VideoResolution) && !string.IsNullOrWhiteSpace(d.VideoResolution)
            ? d.VideoResolution!
            : VideoResolutions.Source;
        PreRollEnabled = d.PreRollSeconds is not null;
        if (d.PreRollSeconds is { } preroll)
        {
            PreRollSeconds = Math.Clamp(preroll, PreRollSecondsMin, PreRollSecondsMax);
        }
        PostRollEnabled = d.PostRollSeconds is > 0;
        if (d.PostRollSeconds is { } postroll && postroll > 0)
        {
            PostRollSeconds = Math.Clamp(postroll, PostRollSecondsMin, PostRollSecondsMax);
        }

        // Audio tracks need a temporary suspend on PersistUserPrefs to avoid a
        // burst of writes during initial population.
        foreach (var entry in AudioTracks)
        {
            entry.PropertyChanged -= OnAudioTrackEntryChanged;
        }
        AudioTracks.CollectionChanged -= OnAudioTracksCollectionChanged;
        AudioTracks.Clear();
        if (d.AudioTracks.Count == 0)
        {
            AudioTracks.Add(new AudioTrackEntry("default_output"));
        }
        else
        {
            foreach (var src in d.AudioTracks)
            {
                AudioTracks.Add(new AudioTrackEntry(src));
            }
        }
        AudioTracks.CollectionChanged += OnAudioTracksCollectionChanged;
        foreach (var entry in AudioTracks)
        {
            entry.PropertyChanged += OnAudioTrackEntryChanged;
        }
    }

    private DaemonConfig CurrentDaemonConfig() => new()
    {
        DaemonExecutablePath = DaemonExecutablePath?.Trim() ?? "",
        LogDirectory = LogDirectory?.Trim() ?? "",
        LogPattern = string.IsNullOrWhiteSpace(LogPattern) ? "WoWCombatLog-*.txt" : LogPattern.Trim(),
        OutputDirectory = OutputDirectory?.Trim() ?? "",
        DatabasePath = DaemonDatabasePath?.Trim() ?? "",
        GpuScreenRecorderExecutable = string.IsNullOrWhiteSpace(GpuScreenRecorderExecutable)
            ? "gpu-screen-recorder" : GpuScreenRecorderExecutable.Trim(),
        CaptureTarget = string.IsNullOrWhiteSpace(CaptureTarget) ? "focused" : CaptureTarget.Trim(),
        Framerate = Framerate <= 0 ? 60 : Framerate,
        Codec = string.IsNullOrWhiteSpace(Codec) ? "h264" : Codec.Trim(),
        Container = string.IsNullOrWhiteSpace(Container) ? "mp4" : Container.Trim(),
        AudioTracks = SnapshotAudioTracks(),
        VideoQuality = string.IsNullOrWhiteSpace(VideoQuality) ? "medium" : VideoQuality.Trim(),
        VideoResolution = string.IsNullOrWhiteSpace(VideoResolution) ||
                          string.Equals(VideoResolution, VideoResolutions.Source, StringComparison.OrdinalIgnoreCase)
            ? null
            : VideoResolution.Trim(),
        PreRollSeconds = PreRollEnabled
            ? Math.Clamp(PreRollSeconds, PreRollSecondsMin, PreRollSecondsMax)
            : (int?)null,
        PostRollSeconds = PostRollEnabled
            ? Math.Clamp(PostRollSeconds, PostRollSecondsMin, PostRollSecondsMax)
            : (int?)null,
    };

    private void ApplyRecordingFilters(RecordingFilters f)
    {
        RecordRaids = f.RecordRaids;
        RecordMythicPlus = f.RecordMythicPlus;
        RecordBattlegrounds = f.RecordBattlegrounds;
        MinKeystoneLevel = Math.Clamp(f.MinKeystoneLevel, MinKeystoneLevelMin, MinKeystoneLevelMax);
        RaidDifficultyChips.Populate(
            AllRaidDifficulties, f.RaidDifficulties,
            SelectedRaidDifficulties, AvailableRaidDifficulties);
    }

    private RecordingFilters CurrentRecordingFilters() => new()
    {
        RecordRaids = RecordRaids,
        RecordMythicPlus = RecordMythicPlus,
        RecordBattlegrounds = RecordBattlegrounds,
        MinKeystoneLevel = Math.Clamp(MinKeystoneLevel, MinKeystoneLevelMin, MinKeystoneLevelMax),
        RaidDifficulties = RaidDifficultyChips.Project(AllRaidDifficulties, SelectedRaidDifficulties),
    };

    private void OnSelectedRaidDifficultiesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        PersistUserPrefs();

    private IReadOnlyList<string> SnapshotAudioTracks()
    {
        var list = new List<string>(AudioTracks.Count);
        foreach (var entry in AudioTracks)
        {
            var src = entry.Source?.Trim();
            if (!string.IsNullOrEmpty(src)) list.Add(src);
        }
        if (list.Count == 0) list.Add("default_output");
        return list;
    }

    partial void OnTimelineSeekBackSecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, TimelineSeekBackMin, TimelineSeekBackMax);
        if (clamped != value)
        {
            TimelineSeekBackSeconds = clamped;
            return;
        }
        PersistTimelinePrefs();
    }

    partial void OnTimelinePauseOnClickChanged(bool value) => PersistTimelinePrefs();

    private void PersistTimelinePrefs()
    {
        _player.UpdateTimelinePreferences(
            new TimelinePreferences(TimelineSeekBackSeconds, TimelinePauseOnClick));
    }

    partial void OnRecordRaidsChanged(bool value) => PersistUserPrefs();
    partial void OnRecordMythicPlusChanged(bool value) => PersistUserPrefs();
    partial void OnRecordBattlegroundsChanged(bool value) => PersistUserPrefs();

    partial void OnMinKeystoneLevelChanged(int value)
    {
        var clamped = Math.Clamp(value, MinKeystoneLevelMin, MinKeystoneLevelMax);
        if (clamped != value)
        {
            MinKeystoneLevel = clamped; // re-enters with the clamped value
            return;
        }
        PersistUserPrefs();
    }

    partial void OnMinFreeGigabytesChanged(long value)
    {
        var clamped = Math.Clamp(value, MinFreeGigabytesMin, MinFreeGigabytesMax);
        if (clamped != value)
        {
            MinFreeGigabytes = clamped; // re-enters with the clamped value
            return;
        }
        PersistUserPrefs();
    }

    /// <summary>
    /// Probe the drive behind the output directory and set the floor's upper
    /// bound to the disk size, then clamp the current value down if it now
    /// exceeds it. Cheap (a DriveInfo lookup); runs on load and whenever the
    /// output dir changes so you can't keep more free than the disk holds.
    /// </summary>
    private void RecomputeStorageMax()
    {
        var disk = SystemDiskSpaceProbe.Instance.Probe(OutputDirectory ?? string.Empty);
        MinFreeGigabytesMax = MaxFreeGigabytesForDisk(disk, MinFreeGigabytesMin);
        if (MinFreeGigabytes > MinFreeGigabytesMax)
        {
            MinFreeGigabytes = MinFreeGigabytesMax;
        }
    }

    partial void OnNotifyOnClipSavedChanged(bool value) => PersistUserPrefs();
    partial void OnNotifyOnErrorsChanged(bool value) => PersistUserPrefs();
    partial void OnCloseToTrayChanged(bool value) => PersistUserPrefs();
    partial void OnDaemonExecutablePathChanged(string value) => PersistUserPrefs();
    partial void OnLogDirectoryChanged(string value) => PersistUserPrefs();
    partial void OnLogPatternChanged(string value) => PersistUserPrefs();
    partial void OnOutputDirectoryChanged(string value)
    {
        RecomputeStorageMax();
        PersistUserPrefs();
    }
    partial void OnDaemonDatabasePathChanged(string value) => PersistUserPrefs();
    partial void OnGpuScreenRecorderExecutableChanged(string value) => PersistUserPrefs();
    partial void OnCaptureTargetChanged(string value) => PersistUserPrefs();
    partial void OnFramerateChanged(int value) => PersistUserPrefs();
    partial void OnCodecChanged(string value) => PersistUserPrefs();
    partial void OnContainerChanged(string value) => PersistUserPrefs();
    partial void OnVideoQualityChanged(string value) => PersistUserPrefs();
    partial void OnVideoResolutionChanged(string value) => PersistUserPrefs();
    partial void OnPreRollEnabledChanged(bool value) => PersistUserPrefs();
    partial void OnPreRollSecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, PreRollSecondsMin, PreRollSecondsMax);
        if (clamped != value)
        {
            PreRollSeconds = clamped;
            return;
        }
        PersistUserPrefs();
    }

    partial void OnPostRollEnabledChanged(bool value) => PersistUserPrefs();
    partial void OnPostRollSecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, PostRollSecondsMin, PostRollSecondsMax);
        if (clamped != value)
        {
            PostRollSeconds = clamped;
            return;
        }
        PersistUserPrefs();
    }

    partial void OnTestRecordingSecondsChanged(int value)
    {
        var clamped = Math.Clamp(value, TestRecordingSecondsMin, TestRecordingSecondsMax);
        if (clamped != value)
        {
            TestRecordingSeconds = clamped;
            return;
        }
        PersistUserPrefs();
    }

    partial void OnChatOverlayEnabledChanged(bool value) => PersistUserPrefs();

    partial void OnChatOverlayXChanged(int value) => ClampOverlayPos(value, v => ChatOverlayX = v);
    partial void OnChatOverlayYChanged(int value) => ClampOverlayPos(value, v => ChatOverlayY = v);
    partial void OnChatOverlayWidthChanged(int value) => ClampOverlaySize(value, v => ChatOverlayWidth = v);
    partial void OnChatOverlayHeightChanged(int value) => ClampOverlaySize(value, v => ChatOverlayHeight = v);

    private void ClampOverlayPos(int value, Action<int> setter)
    {
        var clamped = Math.Clamp(value, ChatOverlayPosMin, ChatOverlayMax);
        if (clamped != value) { setter(clamped); return; }
        PersistUserPrefs();
    }

    private void ClampOverlaySize(int value, Action<int> setter)
    {
        var clamped = Math.Clamp(value, ChatOverlaySizeMin, ChatOverlayMax);
        if (clamped != value) { setter(clamped); return; }
        PersistUserPrefs();
    }

    partial void OnServiceStateChanged(ServiceInstallState value)
    {
        OnPropertyChanged(nameof(IsNotInstalled));
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(ShowReinstallButton));
        RecomputePendingUnitChanges();
    }

    partial void OnHasPendingUnitChangesChanged(bool value)
        => OnPropertyChanged(nameof(ShowReinstallButton));

    /// <summary>
    /// Flag whether the installed unit file is stale relative to the current
    /// daemon settings. The installer writes exactly
    /// <see cref="SystemdUnitFileBuilder.Build"/>, so re-running the builder on
    /// the current config and comparing text is an exact, false-positive-free
    /// signal. Recording filters never appear in the unit, so changing them
    /// leaves this false (they hot-reload instead).
    /// </summary>
    private void RecomputePendingUnitChanges()
    {
        if (!IsInstalled)
        {
            HasPendingUnitChanges = false;
            return;
        }
        try
        {
            var path = _installer.ServiceFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                HasPendingUnitChanges = false;
                return;
            }
            var onDisk = NormalizeUnit(File.ReadAllText(path));
            var expected = NormalizeUnit(SystemdUnitFileBuilder.Build(CurrentDaemonConfig()));
            HasPendingUnitChanges = !string.Equals(onDisk, expected, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not compare installed unit file for pending changes.");
            HasPendingUnitChanges = false;
        }
    }

    private static string NormalizeUnit(string text) =>
        text.Replace("\r\n", "\n").Trim();

    private void PersistUserPrefs()
    {
        if (_suppressPersist) return;
        _userPrefsService.Save(new UserPreferences
        {
            NotifyOnClipSaved = NotifyOnClipSaved,
            NotifyOnErrors = NotifyOnErrors,
            CloseToTray = CloseToTray,
            TestRecordingSeconds = TestRecordingSeconds,
            Daemon = CurrentDaemonConfig(),
            RecordingFilters = CurrentRecordingFilters(),
            StorageManagement = new StorageManagement
            {
                MinFreeGigabytes = Math.Clamp(
                    MinFreeGigabytes, MinFreeGigabytesMin, MinFreeGigabytesMax),
            },
            // Deliberately NOT part of CurrentDaemonConfig — keeping the overlay
            // out of the systemd unit is what stops the reinstall banner from
            // tripping on overlay-only edits (the daemon reads it from prefs).
            ChatOverlay = new ChatOverlay
            {
                Enabled = ChatOverlayEnabled,
                X = Math.Clamp(ChatOverlayX, ChatOverlayPosMin, ChatOverlayMax),
                Y = Math.Clamp(ChatOverlayY, ChatOverlayPosMin, ChatOverlayMax),
                Width = Math.Clamp(ChatOverlayWidth, ChatOverlaySizeMin, ChatOverlayMax),
                Height = Math.Clamp(ChatOverlayHeight, ChatOverlaySizeMin, ChatOverlayMax),
            },
        });
        RecomputePendingUnitChanges();
    }

    [RelayCommand]
    private async Task ReindexAsync()
    {
        IsWorking = true;
        StatusMessage = "Indexing…";
        try
        {
            var result = await _indexer.ReconcileAsync(OutputDirectory, CancellationToken.None);
            StatusMessage = $"Added {result.ClipsAdded}, removed {result.ClipsRemoved}, errors {result.Errors}.";
            await _list.RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Re-index failed");
            StatusMessage = $"Re-index failed: {ex.Message}";
        }
        finally
        {
            IsWorking = false;
        }
    }

    [RelayCommand]
    private async Task RefreshServiceStateAsync()
    {
        try
        {
            ServiceState = await _installer.GetStateAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read service state.");
            ServiceState = ServiceInstallState.NotInstalled;
        }
    }

    [RelayCommand]
    private async Task InstallServiceAsync()
    {
        await RunServiceOperationAsync(
            "Installing service…",
            ct => _installer.InstallAsync(CurrentDaemonConfig(), ct));
    }

    [RelayCommand]
    private async Task UninstallServiceAsync()
    {
        await RunServiceOperationAsync(
            "Uninstalling service…",
            ct => _installer.UninstallAsync(ct));
    }

    [RelayCommand]
    private async Task ReinstallServiceAsync()
    {
        await RunServiceOperationAsync(
            "Rewriting unit file and restarting…",
            ct => _installer.ReinstallAsync(CurrentDaemonConfig(), ct));
    }

    [RelayCommand]
    private Task ViewDaemonLogsAsync() => DaemonStatus.ViewLogsCommand.ExecuteAsync(null);

    private async Task RunServiceOperationAsync(
        string pendingMessage,
        Func<CancellationToken, Task<ServiceOperationResult>> op)
    {
        IsServiceWorking = true;
        ServiceMessage = pendingMessage;
        try
        {
            // Persist preferences before any operation that touches the unit file.
            PersistUserPrefs();
            var result = await op(CancellationToken.None);
            ServiceMessage = result.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service operation failed.");
            ServiceMessage = $"Operation failed: {ex.Message}";
        }
        finally
        {
            IsServiceWorking = false;
            await RefreshServiceStateAsync();
            // ServiceState often doesn't change across a reinstall (Active→Active),
            // so OnServiceStateChanged doesn't fire. Recompute explicitly so the
            // pending-changes banner clears once the on-disk unit matches again.
            RecomputePendingUnitChanges();
        }
    }

    [RelayCommand]
    private async Task RunTestRecordingAsync()
    {
        if (IsTestRecording) return;

        var cfg = CurrentDaemonConfig();
        if (string.IsNullOrWhiteSpace(cfg.OutputDirectory))
        {
            TestRecordingMessage = "Set an output directory first.";
            return;
        }

        try
        {
            Directory.CreateDirectory(cfg.OutputDirectory);
        }
        catch (Exception ex)
        {
            TestRecordingMessage = $"Cannot write to output directory: {ex.Message}";
            return;
        }

        var ext = string.IsNullOrWhiteSpace(cfg.Container) ? "mp4" : cfg.Container.Trim();
        var stem = $"wipelog-test-{DateTime.Now:yyyyMMdd-HHmmss}";
        var outputPath = Path.Combine(cfg.OutputDirectory, $"{stem}.{ext}");

        var options = new GpuScreenRecorderOptions(
            ExecutablePath: cfg.GpuScreenRecorderExecutable,
            Window: cfg.CaptureTarget,
            Framerate: cfg.Framerate,
            Codec: cfg.Codec,
            AudioTracks: cfg.AudioTracks,
            Container: cfg.Container,
            Quality: cfg.VideoQuality);

        var recorder = new GpuScreenRecorder(options, _loggerFactory.CreateLogger<GpuScreenRecorder>());
        string? failureTail = null;
        recorder.ExitedUnexpectedly += (_, e) =>
        {
            failureTail = string.IsNullOrWhiteSpace(e.StderrTail)
                ? $"gpu-screen-recorder exited with code {e.ExitCode}."
                : $"gpu-screen-recorder exited (code {e.ExitCode}). Last stderr:\n{e.StderrTail}";
        };

        var cts = new CancellationTokenSource();
        _testRecordingCts = cts;
        IsTestRecording = true;
        var seconds = Math.Clamp(TestRecordingSeconds, TestRecordingSecondsMin, TestRecordingSecondsMax);
        TestRecordingMessage = $"Recording for {seconds}s…";

        try
        {
            await recorder.StartAsync(outputPath, CancellationToken.None).ConfigureAwait(true);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // User pressed Cancel — fall through to Stop and report cancellation.
            }

            // Always stop on a fresh token so a cancel doesn't tear down the stop itself.
            await recorder.StopAsync(CancellationToken.None).ConfigureAwait(true);

            if (failureTail is not null)
            {
                TestRecordingMessage = failureTail;
            }
            else if (File.Exists(outputPath))
            {
                var size = new FileInfo(outputPath).Length;
                var prefix = cts.IsCancellationRequested ? "Cancelled. Partial clip" : "Test clip saved";
                TestRecordingMessage = $"{prefix} ({FormatBytes(size)}): {outputPath}";
            }
            else
            {
                TestRecordingMessage = "Recorder stopped but no file was produced. Check gpu-screen-recorder logs.";
            }
        }
        catch (FileNotFoundException ex)
        {
            TestRecordingMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Test recording failed.");
            TestRecordingMessage = $"Test recording failed: {ex.Message}";
        }
        finally
        {
            await recorder.DisposeAsync().ConfigureAwait(true);
            cts.Dispose();
            _testRecordingCts = null;
            IsTestRecording = false;
        }
    }

    [RelayCommand]
    private void CancelTestRecording()
    {
        _testRecordingCts?.Cancel();
    }

    /// <summary>
    /// Screenshots the monitor chosen in <see cref="CaptureTarget"/> and paints
    /// the configured mask rectangle on it in a single ffmpeg pass, then opens
    /// the result so the user can iterate on the X/Y/W/H values. Cropping to the
    /// captured monitor (via xrandr offsets) matches the frame the recorder
    /// actually masks, so the bottom-anchored Y lands where it will in clips. No
    /// real-time preview — just a snapshot. Fails gracefully (status line, no
    /// throw) when ffmpeg is missing or the capture fails (e.g. on Wayland, where
    /// x11grab can't grab).
    /// </summary>
    [RelayCommand]
    private async Task TestMaskAsync()
    {
        if (ChatOverlayWidth <= 0 || ChatOverlayHeight <= 0)
        {
            TestMaskMessage = "Width and height must be greater than 0.";
            return;
        }

        var display = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrWhiteSpace(display))
        {
            TestMaskMessage = "No X11 DISPLAY found — the test snapshot needs an X11 session.";
            return;
        }

        TestMaskMessage = "Capturing screen…";

        // Crop the grab to the monitor selected in "Capture target" so the
        // snapshot is the same frame the recorder masks (and the bottom-anchored
        // mask lands against that monitor's bottom edge). When the monitor can't
        // be located — xrandr missing, or a non-monitor target like portal/region
        // with no usable geometry — fall back to the whole X11 root.
        string grabInput = display;
        string? videoSize = null;
        string targetNote = "full display";
        try
        {
            var monitors = await XrandrMonitorList.QueryAsync(CancellationToken.None).ConfigureAwait(true);
            var target = monitors.FirstOrDefault(m => m.Name == CaptureTarget)
                ?? XrandrMonitorList.PickLargest(monitors);
            if (target is not null)
            {
                grabInput = $"{display}+{target.X},{target.Y}";
                videoSize = $"{target.Width}x{target.Height}";
                targetNote = $"{target.Name} {target.Width}x{target.Height}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve monitor geometry for test mask; using full display.");
        }

        var outputPath = Path.Combine(Path.GetTempPath(), "wipelog-test-mask.png");
        // When the user has chosen a downscaled output resolution, mirror that
        // in the preview: scale the grab to the target size, then paint the
        // mask at the matching scaled coordinates. Matches what the daemon
        // actually writes (it scales during gsr encode, then re-encodes with
        // the same scaled drawbox in ChatOverlayProcessor).
        var overlay = new ChatOverlay
        {
            X = ChatOverlayX,
            Y = ChatOverlayY,
            Width = ChatOverlayWidth,
            Height = ChatOverlayHeight,
        };
        double previewScale = 1.0;
        string? previewScaleFilter = null;
        var targetHeight = VideoResolutions.TargetHeight(VideoResolution);
        if (targetHeight is int th && videoSize is not null)
        {
            // videoSize is "WxH" of the monitor we just measured via xrandr.
            var x = videoSize.IndexOf('x');
            if (x > 0
                && int.TryParse(videoSize.AsSpan(0, x), out var monW)
                && int.TryParse(videoSize.AsSpan(x + 1), out var monH)
                && monW > 0 && monH > 0 && th < monH)
            {
                var outW = (int)Math.Round((double)monW * th / monH);
                if (outW % 2 != 0) outW += 1;
                previewScale = (double)th / monH;
                previewScaleFilter = $"scale={outW}:{th}";
                targetNote += $" → preview {outW}x{th}";
            }
        }
        var box = ChatOverlayProcessor.BuildDrawboxFilter(overlay, previewScale);
        var filter = previewScaleFilter is null ? box : $"{previewScaleFilter},{box}";

        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("x11grab");
        if (videoSize is not null)
        {
            psi.ArgumentList.Add("-video_size"); psi.ArgumentList.Add(videoSize);
        }
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(grabInput);
        psi.ArgumentList.Add("-frames:v"); psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-vf"); psi.ArgumentList.Add(filter);
        psi.ArgumentList.Add(outputPath);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                TestMaskMessage = "Could not launch ffmpeg.";
                return;
            }
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync().ConfigureAwait(true);
            if (proc.ExitCode != 0 || !File.Exists(outputPath))
            {
                var stderr = (await stderrTask.ConfigureAwait(true)).Trim();
                TestMaskMessage = string.IsNullOrEmpty(stderr)
                    ? "Screen capture failed (is this an X11 session?)."
                    : $"Screen capture failed: {stderr}";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("xdg-open", outputPath) { UseShellExecute = false });
                TestMaskMessage = $"Opened preview ({targetNote}): {outputPath}";
            }
            catch (Exception ex)
            {
                TestMaskMessage = $"Saved {outputPath} but could not open a viewer: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Test mask capture failed.");
            TestMaskMessage = $"Test mask failed: {ex.Message}";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes / (1024.0 * 1024.0):0.##} MB";
    }

    [RelayCommand]
    private void AddAudioTrack(string? source)
    {
        var src = string.IsNullOrWhiteSpace(source) ? "default_output" : source.Trim();
        AudioTracks.Add(new AudioTrackEntry(src));
    }

    [RelayCommand]
    private void RemoveAudioTrack(AudioTrackEntry? entry)
    {
        if (entry is null) return;
        AudioTracks.Remove(entry);
    }

    [RelayCommand]
    private void ApplyAudioPreset()
    {
        // The "split-by-app" preset: WoW / Spotify / Discord on isolated
        // tracks plus a full-system mix as a safety net.
        //
        // Track 4 used to be `app-inverse:WoW|spotify|Discord` (intersect
        // "everything except those three"), but gpu-screen-recorder treats
        // `|` as the source-union separator at the -a argument level —
        // `app-inverse:` only accepts ONE app name. So the value parsed as
        // three sources and the bare `spotify` failed with "not a valid
        // audio device". `default_output` (full mix incl. the others as
        // duplicates) is the reliable fallback; the duplicates can be muted
        // in post.
        var presets = new[]
        {
            "app:World of Warcraft",
            "app:spotify",
            "app:WEBRTC VoiceEngine",
            "default_output",
        };

        AudioTracks.Clear();
        foreach (var src in presets)
        {
            AudioTracks.Add(new AudioTrackEntry(src));
        }
    }

    [RelayCommand]
    private Task RefreshAudioSuggestionsAsync() => LoadAudioSuggestionsAsync();

    private async Task LoadAudioSuggestionsAsync()
    {
        var exe = string.IsNullOrWhiteSpace(GpuScreenRecorderExecutable)
            ? "gpu-screen-recorder"
            : GpuScreenRecorderExecutable;

        var apps = await ListApplicationAudioAsync(exe, CancellationToken.None).ConfigureAwait(false);

        Dispatcher.UIThread.Post(() =>
        {
            AudioSourceSuggestions.Clear();
            AudioSourceSuggestions.Add(new AudioSourceSuggestion("default_output", "System mix (all output)"));
            AudioSourceSuggestions.Add(new AudioSourceSuggestion("default_input", "Microphone (default input)"));
            foreach (var app in apps)
            {
                AudioSourceSuggestions.Add(new AudioSourceSuggestion($"app:{app}", $"App · {app}"));
            }
        });
    }

    private async Task<IReadOnlyList<string>> ListApplicationAudioAsync(string exe, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("--list-application-audio");
            using var proc = Process.Start(psi);
            if (proc is null) return Array.Empty<string>();
            var stdout = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var lines = new List<string>();
            foreach (var raw in stdout.Split('\n'))
            {
                var line = raw.Trim();
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            return lines;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not enumerate application audio sources.");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// First-run onboarding: scan well-known Linux WoW launcher prefixes
    /// (Lutris / Heroic / Bottles / raw Wine) for combat-log directories.
    /// Populates <see cref="DetectedLogPaths"/> so the Settings banner can
    /// render the candidates as click-to-apply chips. Runs the FS walk on
    /// a thread-pool thread so the UI stays responsive even when several
    /// prefixes need traversing.
    /// </summary>
    [RelayCommand]
    private async Task DetectWowLogsAsync()
    {
        IsDetectingWowLogs = true;
        DetectionMessage = string.Empty;
        try
        {
            var hits = await Task.Run(() => _wowLogDetector.Detect()).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DetectedLogPaths.Clear();
                foreach (var hit in hits) DetectedLogPaths.Add(hit);
                OnPropertyChanged(nameof(HasDetectedLogPaths));
                DetectionMessage = hits.Count switch
                {
                    0 => "No WoW combat-log directories detected. Make sure WoW has been " +
                         "launched at least once, and that combat logging is enabled " +
                         "(/combatlog in chat).",
                    1 => "Found 1 candidate.",
                    _ => $"Found {hits.Count} candidates. Newest activity first.",
                };
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Detect WoW logs threw.");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DetectionMessage = $"Detection failed: {ex.Message}";
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsDetectingWowLogs = false);
        }
    }

    /// <summary>
    /// Apply a detected candidate as the daemon's log directory. Persists
    /// to user preferences immediately so the user doesn't have to also
    /// reinstall the service to feel the change land. The banner hides
    /// itself because <see cref="IsOnboardingNeeded"/> turns false.
    /// </summary>
    [RelayCommand]
    private void ApplyDetectedPath(DetectedLogPath? path)
    {
        if (path is null) return;
        LogDirectory = path.Path;
        DetectedLogPaths.Clear();
        OnPropertyChanged(nameof(HasDetectedLogPaths));
        DetectionMessage = string.Empty;
        ActiveTab = SettingsTab.Service;   // park the user where the install button lives
    }
}

/// <summary>
/// One row in the Settings → Recording → Audio tracks editor. Wraps a single
/// gpu-screen-recorder <c>-a</c> value plus a derived display label so the
/// UI can show something friendlier than the raw token.
/// </summary>
public sealed partial class AudioTrackEntry : ObservableObject
{
    public AudioTrackEntry(string source)
    {
        _source = source;
    }

    [ObservableProperty]
    private string _source = "";

    public string DerivedLabel => DeriveLabel(Source);

    partial void OnSourceChanged(string value) => OnPropertyChanged(nameof(DerivedLabel));

    private static string DeriveLabel(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "(empty)";
        var s = source.Trim();
        if (s.Equals("default_output", StringComparison.OrdinalIgnoreCase)) return "System mix";
        if (s.Equals("default_input", StringComparison.OrdinalIgnoreCase)) return "Microphone";
        if (s.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            return s.Substring(4);
        }
        if (s.StartsWith("app-inverse:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = s.Substring("app-inverse:".Length);
            return $"Everything except {rest}";
        }
        if (s.StartsWith("device:", StringComparison.OrdinalIgnoreCase))
        {
            return s.Substring("device:".Length);
        }
        return s;
    }
}

/// <summary>
/// A suggestion shown in the "Add track" flyout. <see cref="Source"/> is the
/// value handed to gpu-screen-recorder; <see cref="Display"/> is what the
/// list item shows.
/// </summary>
public sealed record AudioSourceSuggestion(string Source, string Display);
