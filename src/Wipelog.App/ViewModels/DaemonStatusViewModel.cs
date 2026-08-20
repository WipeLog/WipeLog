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

using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Wipelog.App.Services;
using Wipelog.Core.Status;
using Wipelog.Core.Time;

namespace Wipelog.App.ViewModels;

public sealed partial class DaemonStatusViewModel : ViewModelBase, IDisposable
{
    private static readonly IBrush GreenDot = new SolidColorBrush(Color.Parse("#3fb950"));
    private static readonly IBrush AmberDot = new SolidColorBrush(Color.Parse("#d29922"));
    private static readonly IBrush RedDot = new SolidColorBrush(Color.Parse("#e34c4c"));

    private readonly IDaemonStatusReader _reader;
    private readonly IDaemonLifecycleController _lifecycle;
    private readonly IDaemonControlClient _control;
    private readonly IClock _clock;
    private readonly ILogger<DaemonStatusViewModel> _logger;
    private readonly DispatcherTimer _refreshTimer;

    [ObservableProperty]
    private string _stateLabel = "Offline";

    [ObservableProperty]
    private IBrush _dotBrush = RedDot;

    [ObservableProperty]
    private string _tooltip = "Daemon offline";

    [ObservableProperty]
    private bool _isOffline = true;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isIdle;

    // Surfaced directly on the sidebar Status pill while recording so the
    // user sees what's being captured without hovering for the tooltip.
    // Empty string when not recording — bindings hide via IsRecording.
    [ObservableProperty]
    private string _activityLine = string.Empty;

    [ObservableProperty]
    private string _elapsedLine = string.Empty;

    /// <summary>
    /// Mirrors <see cref="DaemonStatus.AdvancedLoggingDisabled"/>. Sidebar
    /// binds a warning row to this; stays true until the daemon parses a
    /// <c>COMBAT_LOG_VERSION</c> header with <c>ADVANCED_LOG_ENABLED=1</c>.
    /// </summary>
    [ObservableProperty]
    private bool _advancedLoggingDisabled;

    /// <summary>
    /// Mirrors <see cref="DaemonStatus.RecorderError"/>: the recorder (or the
    /// pre-roll process) failed in a way that loses video. The sidebar binds a
    /// red warning row to <see cref="HasRecorderError"/> with the full message
    /// (incl. gsr's stderr tail) as tooltip; the daemon clears it once a clip
    /// finalizes with a real file.
    /// </summary>
    [ObservableProperty]
    private string _recorderError = string.Empty;

    [ObservableProperty]
    private bool _hasRecorderError;

    /// <summary>
    /// Whole-drive usage as a 0–100% fill for the Storage-tab progress bar.
    /// 0 when the daemon is offline or the drive is unknown.
    /// </summary>
    [ObservableProperty]
    private double _diskUsagePercent;

    /// <summary>"420.0 GB / 500.0 GB · 80.0 GB free", or an offline/placeholder line.</summary>
    [ObservableProperty]
    private string _diskUsageText = "Daemon offline — usage unknown";

    /// <summary>True only when a real measurement is available (gates the readout style).</summary>
    [ObservableProperty]
    private bool _hasDiskUsage;

    /// <summary>Progress-bar fill brush: red as free space nears/drops below the floor.</summary>
    [ObservableProperty]
    private IBrush _diskUsageBarBrush = new SolidColorBrush(Color.Parse("#5865f2"));

    public DaemonStatusViewModel(
        IDaemonStatusReader reader,
        IDaemonLifecycleController lifecycle,
        IDaemonControlClient control,
        IClock clock,
        ILogger<DaemonStatusViewModel> logger)
    {
        _reader = reader;
        _lifecycle = lifecycle;
        _control = control;
        _clock = clock;
        _logger = logger;
        _reader.StatusChanged += OnStatusChanged;
        Apply(_reader.Current);

        // While Recording, refresh once a second so the elapsed-time tooltip
        // ticks even when the underlying status didn't change.
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (IsRecording) Apply(_reader.Current);
        });
        _refreshTimer.Start();
    }

    private void OnStatusChanged(object? sender, DaemonStatus? status)
    {
        Dispatcher.UIThread.Post(() => Apply(status));
    }

    internal readonly record struct Projection(
        string Label,
        IBrush Brush,
        string Tooltip,
        bool Offline,
        bool Recording,
        bool Idle,
        string ActivityLine,
        string ElapsedLine,
        bool AdvancedLoggingDisabled,
        string RecorderError,
        double DiskUsagePercent,
        string DiskUsageText,
        bool HasDiskUsage,
        string DiskBarBrushKey);

    private const double BytesPerGigabyte = 1024.0 * 1024 * 1024;

    /// <summary>
    /// Theme brush key for the usage bar based on how close free space is to the
    /// floor: at/below the floor → danger; within 25% above it → warning; else
    /// the normal accent. Pure for testability.
    /// </summary>
    internal static string DiskBarBrushKey(long freeBytes, long minFreeBytes)
        => freeBytes <= minFreeBytes ? "DangerBrush"
            : freeBytes <= (long)(minFreeBytes * 1.25) ? "WarningBrush"
            : "AccentPrimaryBrush";

    private static (double Percent, string Text, bool Has, string BrushKey) ProjectDisk(DaemonStatus? status)
    {
        if (status is null) return (0, "Daemon offline — usage unknown", false, "AccentPrimaryBrush");
        // TotalBytes == 0 → drive unknown (or a pre-free-space-floor payload).
        if (status.DiskUsage is not { TotalBytes: > 0 } d) return (0, "—", false, "AccentPrimaryBrush");
        var text = string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            "{0:0.0} GB / {1:0.0} GB · {2:0.0} GB free",
            d.UsedBytes / BytesPerGigabyte, d.TotalBytes / BytesPerGigabyte, d.FreeBytes / BytesPerGigabyte);
        return (d.UsagePercent, text, true, DiskBarBrushKey(d.FreeBytes, d.MinFreeBytes));
    }

    internal static Projection Project(DaemonStatus? status, DateTimeOffset now)
    {
        var (diskPercent, diskText, hasDisk, diskBrushKey) = ProjectDisk(status);
        if (status is null)
        {
            return new Projection(
                "Offline", RedDot,
                "Daemon offline — heartbeat not seen in the last 10s.",
                Offline: true, Recording: false, Idle: false,
                ActivityLine: string.Empty, ElapsedLine: string.Empty,
                AdvancedLoggingDisabled: false,
                RecorderError: string.Empty,
                DiskUsagePercent: diskPercent, DiskUsageText: diskText, HasDiskUsage: hasDisk, DiskBarBrushKey: diskBrushKey);
        }

        if (status.State == DaemonState.Recording && status.CurrentRecording is { } rec)
        {
            var elapsed = now - rec.StartedAt;
            var activity = ComposeActivityLine(rec);
            var elapsedStr = elapsed.TotalSeconds > 0
                ? FormatDuration(elapsed)
                : "0:00";

            var tip = $"Recording: {activity}";
            if (elapsed.TotalSeconds > 0)
            {
                tip += $" ({elapsedStr})";
            }
            if (!string.IsNullOrEmpty(status.LastError))
            {
                tip += $"\nLast error: {status.LastError}";
            }
            return new Projection(
                "Recording", GreenDot, tip,
                Offline: false, Recording: true, Idle: false,
                ActivityLine: activity, ElapsedLine: elapsedStr,
                AdvancedLoggingDisabled: status.AdvancedLoggingDisabled,
                RecorderError: status.RecorderError ?? string.Empty,
                DiskUsagePercent: diskPercent, DiskUsageText: diskText, HasDiskUsage: hasDisk, DiskBarBrushKey: diskBrushKey);
        }

        var idleTip = status.ClipsRecordedThisSession == 1
            ? "Idle · 1 clip this session"
            : $"Idle · {status.ClipsRecordedThisSession} clips this session";
        if (!string.IsNullOrEmpty(status.LastError))
        {
            idleTip += $"\nLast error: {status.LastError}";
        }
        return new Projection(
            "Idle", AmberDot, idleTip,
            Offline: false, Recording: false, Idle: true,
            ActivityLine: string.Empty, ElapsedLine: string.Empty,
            AdvancedLoggingDisabled: status.AdvancedLoggingDisabled,
            RecorderError: status.RecorderError ?? string.Empty,
            DiskUsagePercent: diskPercent, DiskUsageText: diskText, HasDiskUsage: hasDisk, DiskBarBrushKey: diskBrushKey);
    }

    private void Apply(DaemonStatus? status)
    {
        var p = Project(status, _clock.Now);
        StateLabel = p.Label;
        DotBrush = p.Brush;
        Tooltip = p.Tooltip;
        IsOffline = p.Offline;
        IsRecording = p.Recording;
        IsIdle = p.Idle;
        ActivityLine = p.ActivityLine;
        ElapsedLine = p.ElapsedLine;
        AdvancedLoggingDisabled = p.AdvancedLoggingDisabled;
        RecorderError = p.RecorderError;
        HasRecorderError = p.RecorderError.Length > 0;
        DiskUsagePercent = p.DiskUsagePercent;
        DiskUsageText = p.DiskUsageText;
        HasDiskUsage = p.HasDiskUsage;
        DiskUsageBarBrush = ResolveBrush(p.DiskBarBrushKey);
    }

    private static readonly IBrush AccentFallback = new SolidColorBrush(Color.Parse("#5865f2"));

    /// <summary>Resolve a theme brush key to a brush, tracking the active theme variant.</summary>
    private static IBrush ResolveBrush(string key)
    {
        var app = Avalonia.Application.Current;
        if (app is not null &&
            app.TryGetResource(key, app.ActualThemeVariant, out var res) &&
            res is IBrush brush)
        {
            return brush;
        }
        return AccentFallback;
    }

    /// <summary>
    /// Friendly one-line label for the activity. M+ tacks on the keystone
    /// level; raids/arena/BG use the display name verbatim (boss name,
    /// bracket, zone) since that's already the right read.
    /// </summary>
    private static string ComposeActivityLine(RecordingStatus rec)
    {
        if (rec.KeystoneLevel is { } k && k > 0)
        {
            return $"{rec.DisplayName} +{k}";
        }
        return rec.DisplayName;
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";

    [RelayCommand]
    private Task StartDaemonAsync() => _lifecycle.StartAsync(CancellationToken.None);

    [RelayCommand]
    private Task StopDaemonAsync() => _lifecycle.StopAsync(CancellationToken.None);

    [RelayCommand]
    private Task RestartDaemonAsync() => _lifecycle.RestartAsync(CancellationToken.None);

    [RelayCommand]
    private Task ViewLogsAsync() => _lifecycle.OpenJournalAsync(CancellationToken.None);

    /// <summary>
    /// Sidebar Stop button: tells the daemon to finalise the current recording.
    /// Manual recordings finalise as success; encounter-triggered ones (raid /
    /// M+ / PvP) finalise as failure (saved with the wipe/depleted/fail suffix
    /// so the user can still review the clip). Daemon-unreachable errors are
    /// logged but not surfaced as a dialog — the sidebar pill itself reflects
    /// the resulting state.
    /// </summary>
    [RelayCommand]
    private async Task StopRecordingAsync()
    {
        try
        {
            var result = await _control.StopRecordingAsync(CancellationToken.None).ConfigureAwait(false);
            switch (result.Status)
            {
                case ControlCallStatus.Success:
                    _logger.LogInformation("Stop recording: daemon acknowledged stop.");
                    break;
                case ControlCallStatus.Failure:
                    _logger.LogInformation("Stop recording: daemon refused — {Error}", result.Error);
                    break;
                case ControlCallStatus.Unreachable:
                    _logger.LogWarning("Stop recording: could not reach daemon — {Error}", result.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stop recording threw unexpectedly.");
        }
    }

    /// <summary>
    /// Sidebar Record button: starts a free-form Manual recording. Surfaces
    /// the same Unreachable/Failure logging as Stop so a user clicking
    /// repeatedly doesn't get spammed with dialogs.
    /// </summary>
    [RelayCommand]
    private async Task StartManualRecordingAsync()
    {
        try
        {
            var result = await _control.StartManualRecordingAsync(CancellationToken.None).ConfigureAwait(false);
            switch (result.Status)
            {
                case ControlCallStatus.Success:
                    _logger.LogInformation("Start manual recording: daemon acknowledged.");
                    break;
                case ControlCallStatus.Failure:
                    _logger.LogInformation("Start manual recording: daemon refused — {Error}", result.Error);
                    break;
                case ControlCallStatus.Unreachable:
                    _logger.LogWarning("Start manual recording: could not reach daemon — {Error}", result.Error);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Start manual recording threw unexpectedly.");
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _reader.StatusChanged -= OnStatusChanged;
    }
}
