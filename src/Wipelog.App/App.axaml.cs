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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SukiUI;
using SukiUI.Models;
using Wipelog.App.Services;
using Wipelog.App.ViewModels;
using Wipelog.App.Views;

namespace Wipelog.App;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    private TrayIconHost? _trayIconHost;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // Dev tools are wired in Program.cs via AppBuilder.WithDeveloperTools()
        // — attaching here too would throw "already attached" on Avalonia 11.3+.
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
        }
        catch (Exception ex)
        {
            ShowVlcInitFailureAndExit(ex);
            return;
        }

        ApplyTheme();

        Services = AppServices.Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            // Kick off the initial library load after the window is shown so a
            // slow disk doesn't block the splash.
            _ = vm.InitializeAsync();

            // ShutdownMode.OnExplicitShutdown — closing the main window
            // shouldn't quit the app, just hide it to the tray. Quit goes
            // through the tray menu or Ctrl+Q.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _trayIconHost = new TrayIconHost(
                Services.GetRequiredService<IDaemonStatusReader>(),
                Services.GetRequiredService<IDaemonLifecycleController>(),
                vm.OutputDirectory,
                showMainWindow: () => ShowMainWindow(desktop),
                quitApp: () => Quit(desktop),
                logger: Services.GetService<ILoggerFactory>()?.CreateLogger<TrayIconHost>());
            _trayIconHost.Initialize();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Register and select the Wipelog blurple color theme, locked to dark.
    // SukiTheme drives the accent across all SukiUI controls (buttons, side
    // menu selection, glass-card highlights, toasts).
    private static void ApplyTheme()
    {
        var theme = SukiTheme.GetInstance();
        var blurple = new SukiColorTheme(
            "Wipelog",
            Color.Parse("#5865f2"),   // primary accent (Discord blurple)
            Color.Parse("#8b5cf6"));  // secondary accent (violet)
        theme.AddColorTheme(blurple);
        theme.ChangeColorTheme(blurple);
        theme.ChangeBaseTheme(ThemeVariant.Dark);
    }

    private static void ShowMainWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is null) return;
        desktop.MainWindow.Show();
        desktop.MainWindow.WindowState = WindowState.Normal;
        desktop.MainWindow.Activate();
    }

    private void Quit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _trayIconHost?.Dispose();
        _trayIconHost = null;
        if (desktop.MainWindow is Views.MainWindow main) main.RequestRealClose();
        desktop.Shutdown();
        // Skip CLR finalizer + LibVLC native-thread teardown — those add a
        // multi-second tail that Ctrl+C avoids by killing the process outright.
        Environment.Exit(0);
    }

    private void ShowVlcInitFailureAndExit(Exception ex)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Window
            {
                Title = "Wipelog — VLC required",
                Width = 460, Height = 220,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = "VLC is required for video playback.", FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 16 },
                        new TextBlock { Text = "Install it with one of:", Opacity = 0.8 },
                        new TextBlock { Text = "  sudo pacman -S vlc      # Arch", FontFamily = "monospace" },
                        new TextBlock { Text = "  sudo apt install vlc    # Debian/Ubuntu", FontFamily = "monospace" },
                        new TextBlock { Text = "  sudo dnf install vlc    # Fedora", FontFamily = "monospace" },
                        new TextBlock { Text = ex.Message, Opacity = 0.6, FontSize = 11 },
                    },
                },
            };
            desktop.MainWindow = window;
        }
    }
}
