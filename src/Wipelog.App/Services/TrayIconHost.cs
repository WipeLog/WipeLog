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

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Status;

namespace Wipelog.App.Services;

internal sealed class TrayIconHost : IDisposable
{
    private readonly IDaemonStatusReader _statusReader;
    private readonly IDaemonLifecycleController _lifecycle;
    private readonly ILogger<TrayIconHost>? _logger;
    private readonly string _clipOutputDirectory;
    private readonly Action _showMainWindow;
    private readonly Action _quitApp;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusHeader;
    private NativeMenuItem? _startItem;
    private NativeMenuItem? _stopItem;
    private NativeMenuItem? _restartItem;

    public TrayIconHost(
        IDaemonStatusReader statusReader,
        IDaemonLifecycleController lifecycle,
        string clipOutputDirectory,
        Action showMainWindow,
        Action quitApp,
        ILogger<TrayIconHost>? logger = null)
    {
        _statusReader = statusReader;
        _lifecycle = lifecycle;
        _clipOutputDirectory = clipOutputDirectory;
        _showMainWindow = showMainWindow;
        _quitApp = quitApp;
        _logger = logger;
    }

    public void Initialize()
    {
        _trayIcon = new TrayIcon { ToolTipText = "Wipelog" };
        _trayIcon.Menu = BuildMenu();
        _trayIcon.Clicked += (_, _) => _showMainWindow();

        var app = Application.Current
            ?? throw new InvalidOperationException("Application.Current is null — call Initialize after the framework starts.");
        var icons = TrayIcon.GetIcons(app) ?? new TrayIcons();
        icons.Add(_trayIcon);
        TrayIcon.SetIcons(app, icons);

        ApplyStatus(_statusReader.Current);
        _statusReader.StatusChanged += OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, DaemonStatus? status)
    {
        Dispatcher.UIThread.Post(() => ApplyStatus(status));
    }

    internal static (TrayIconKind Icon, string Tooltip, TrayMenuState MenuState) ComputeIconAndTooltip(
        DaemonStatus? status,
        DateTimeOffset now)
    {
        if (status is null)
        {
            return (TrayIconKind.Offline, "Wipelog — daemon offline", TrayMenuState.Offline);
        }

        if (status.State == DaemonState.Recording && status.CurrentRecording is { } rec)
        {
            var elapsed = now - rec.StartedAt;
            var label = $"Recording: {rec.DisplayName}";
            if (elapsed.TotalSeconds >= 0)
            {
                label += $" ({FormatDuration(elapsed)})";
            }
            return (TrayIconKind.Recording, label, TrayMenuState.Recording);
        }

        var idleTip = status.ClipsRecordedThisSession == 1
            ? "Idle · 1 clip this session"
            : $"Idle · {status.ClipsRecordedThisSession} clips this session";
        return (TrayIconKind.Idle, idleTip, TrayMenuState.Idle);
    }

    private void ApplyStatus(DaemonStatus? status)
    {
        if (_trayIcon is null) return;
        var (iconKind, tooltip, menuState) = ComputeIconAndTooltip(status, DateTimeOffset.UtcNow);
        _trayIcon.Icon = ResolveIcon(iconKind);
        _trayIcon.ToolTipText = tooltip;
        UpdateMenuForState(menuState, status);
    }

    private static WindowIcon ResolveIcon(TrayIconKind kind) => kind switch
    {
        TrayIconKind.Recording => TrayIconImages.Recording,
        TrayIconKind.Idle => TrayIconImages.Idle,
        _ => TrayIconImages.Offline,
    };

    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();
        var show = new NativeMenuItem("Show Wipelog");
        show.Click += (_, _) => _showMainWindow();
        menu.Items.Add(show);
        menu.Items.Add(new NativeMenuItemSeparator());

        _statusHeader = new NativeMenuItem("Daemon offline") { IsEnabled = false };
        menu.Items.Add(_statusHeader);
        menu.Items.Add(new NativeMenuItemSeparator());

        _startItem = new NativeMenuItem("Start daemon");
        _startItem.Click += (_, _) => _ = _lifecycle.StartAsync(CancellationToken.None);
        menu.Items.Add(_startItem);

        _stopItem = new NativeMenuItem("Stop daemon");
        _stopItem.Click += (_, _) => _ = _lifecycle.StopAsync(CancellationToken.None);
        menu.Items.Add(_stopItem);

        _restartItem = new NativeMenuItem("Restart daemon");
        _restartItem.Click += (_, _) => _ = _lifecycle.RestartAsync(CancellationToken.None);
        menu.Items.Add(_restartItem);

        var openFolder = new NativeMenuItem("Open clip folder");
        openFolder.Click += (_, _) => OpenClipFolder();
        menu.Items.Add(openFolder);

        menu.Items.Add(new NativeMenuItemSeparator());
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => _quitApp();
        menu.Items.Add(quit);

        return menu;
    }

    private void UpdateMenuForState(TrayMenuState state, DaemonStatus? status)
    {
        if (_statusHeader is null || _startItem is null || _stopItem is null || _restartItem is null) return;

        _statusHeader.Header = state switch
        {
            TrayMenuState.Recording when status?.CurrentRecording is { } rec =>
                $"Recording: {rec.DisplayName}",
            TrayMenuState.Idle => "Idle",
            _ => "Daemon offline",
        };

        // Visibility = control which lifecycle option is shown.
        // Offline → only Start. Idle/Recording → Stop + Restart.
        _startItem.IsVisible = state == TrayMenuState.Offline;
        _stopItem.IsVisible = state != TrayMenuState.Offline;
        _restartItem.IsVisible = state != TrayMenuState.Offline;
    }

    private void OpenClipFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", _clipOutputDirectory)
            {
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open clip folder {Path}.", _clipOutputDirectory);
        }
    }

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";

    public void Dispose()
    {
        _statusReader.StatusChanged -= OnStatusChanged;
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    internal enum TrayMenuState { Offline, Idle, Recording }
    internal enum TrayIconKind { Offline, Idle, Recording }
}
