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
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using SukiUI;
using SukiUI.Models;

[assembly: AvaloniaTestApplication(typeof(Wipelog.App.Snapshots.SnapshotApp))]
// Avalonia's platform state (AvaloniaLocator, the avares:// URI parser) is
// process-global; one headless session per test class must not overlap.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Wipelog.App.Snapshots;

/// <summary>
/// The real <see cref="App"/> (same App.axaml resources + SukiUI styles via
/// the inherited <c>Initialize</c>), minus everything in
/// <c>OnFrameworkInitializationCompleted</c> that needs a live machine:
/// LibVLC native init, the DI container (real SQLite DB, systemd, daemon
/// status file), the tray icon and the main-window lifetime. Only the theme
/// selection is kept — duplicated from <c>App.ApplyTheme</c> because that is
/// private and the real App must not change. Keep the two in sync.
/// </summary>
public sealed class SnapshotApp : App
{
    /// <summary>
    /// Found by reflection: <c>HeadlessUnitTestSession.StartNew(typeof(SnapshotApp))</c>
    /// and <c>[AvaloniaTestApplication]</c> both call <c>BuildAvaloniaApp</c>.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SnapshotApp>()
            .UseSkia()
            // Real pixels: UseHeadlessDrawing=true would swap in a stub
            // renderer and CaptureRenderedFrame would return nothing useful.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            // Same embedded avares:// Inter font the real Program.cs registers.
            .WithInterFont();

    /// <summary>
    /// Several views service-locate through <c>App.Services</c>
    /// (ClipsTableView, ClipDetailsView, MainWindow). Its setter is private
    /// and the real App must not change, so tests attach their provider by
    /// reflection on the current (per-dispatch) application instance.
    /// </summary>
    public static void AttachServices(IServiceProvider services)
    {
        var app = Application.Current as App
            ?? throw new InvalidOperationException("No App instance — call from inside HeadlessSessionFixture.Run.");
        typeof(App).GetProperty(nameof(App.Services))!.SetValue(app, services);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var theme = SukiTheme.GetInstance();
        var blurple = new SukiColorTheme(
            "Wipelog",
            Color.Parse("#5865f2"),
            Color.Parse("#8b5cf6"));
        theme.AddColorTheme(blurple);
        theme.ChangeColorTheme(blurple);
        theme.ChangeBaseTheme(ThemeVariant.Dark);
    }
}
