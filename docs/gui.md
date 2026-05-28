# GUI (`Wipelog.App`)

The GUI is an Avalonia 11 desktop application with embedded LibVLC for
playback. It reads the same SQLite library the daemon writes, watches
the output directory for new clips, and is the only place where users
configure recording (Settings → Service tab) or stop a running clip
(sidebar Stop button → daemon control socket).

## Composition

| Layer | Pattern | Notes |
|---|---|---|
| Views | Avalonia XAML with `x:DataType` for compiled bindings | Every view is paired with a `*.axaml.cs` code-behind, but most of the logic lives in the matching view model. |
| View models | `CommunityToolkit.Mvvm` source generators (`[ObservableProperty]`, `[RelayCommand]`) | All view models inherit `ViewModelBase` (= `ObservableObject`). One VM per view, plus a small set of child VMs hung off `MainWindowViewModel`. |
| Services | Constructor-injected via `Microsoft.Extensions.DependencyInjection` | DI graph built in `AppServices.Build()`. Logging through `Microsoft.Extensions.Logging`. |
| Playback | `LibVLCSharp.Avalonia` (`VideoView` control) wrapped by `PlayerViewModel` | LibVLC initialised once in `App.axaml.cs:47` (`LibVLCSharp.Shared.Core.Initialize()`). |

`AppServices.Build()` lives in
[`src/Wipelog.App/AppServices.cs`](../src/Wipelog.App/AppServices.cs).

## View hierarchy

`MainWindow.axaml` lays out a three-column grid with a full-window
overlay for Settings:

```
+---------------------------------------------------------------+
|                       MainWindow                              |
|  +-------+--------------------------------+----------------+  |
|  |       | PlayerView                     |                |  |
|  |       |   - VideoView (LibVLCSharp)    |                |  |
|  |       |   - ScrubBar + TombstoneStrip  |                |  |
|  | SideNav|------------------------------ | ClipDetails    |  |
|  | 220 px | FilterBarView                 |   300 px       |  |
|  |       |   Deaths · Encounters · ★      |                |  |
|  |       |   Search · Date range          |                |  |
|  |       |------------------------------- |                |  |
|  |       | ClipsTableView (DataGrid)      |                |  |
|  +-------+--------------------------------+----------------+  |
|                                                               |
|  SettingsView   (full-window overlay, IsVisible-gated)        |
+---------------------------------------------------------------+
```

The three-column grid and the Settings overlay are siblings of the same
top-level `Grid`. The overlay's `IsVisible` binds to
`MainWindowViewModel.IsShowingSettings`; toggling it does **not**
unload the main view, so playback state is preserved when the user
returns from Settings.

| Region | View | View model | Purpose |
|---|---|---|---|
| Left column (220 px) | `Controls/SideNav.axaml` | `SideNavViewModel`, `DaemonStatusViewModel` | Status pill (idle / recording / error / ACL warning), six Recordings categories with per-type counts, Settings entries docked to the bottom. Hosts the Record / Stop buttons. |
| Centre top | `Views/PlayerView.axaml` | `PlayerViewModel`, `TimelineViewModel` | LibVLC `VideoView` + transport controls + the scrub bar with timeline events. |
| Centre middle | `Views/FilterBarView.axaml` | `FilterBarViewModel`, `ClipFiltersViewModel` | Segmented controls: Deaths (All/Own/None), Encounters (On/Off), Favorites ★, Search box, Date-range button-with-flyout. |
| Centre bottom | `Views/ClipsTableView.axaml` | `ClipListViewModel`, `ClipListItemViewModel` (per row) | `DataGrid` with Extended selection, context-menu (Favorite, Delete, Edit metadata, Archive). |
| Right column (300 px) | `Views/ClipDetailsView.axaml` | `ClipDetailsViewModel` | Content-type-aware metadata grid (12 IsVisible-gated rows), raid-comp chip strip, free-form notes editor. |
| Overlay | `Views/SettingsView.axaml` | `SettingsViewModel`, `DisplayOptionsViewModel` | Three tabs (Service, General, Display). Welcome banner when `LogDirectory` is empty. |

CLAUDE.md's "First-run onboarding", "Clip details panel", "Clip notes",
and "Filter bar" sections cover the specific behaviours within these
regions; everything below is the structural and platform-level picture
that doesn't fit there.

## Library watcher

The GUI never asks the daemon "what clips do you know about." Instead,
`LibraryWatcher`
([`src/Wipelog.App/Services/LibraryWatcher.cs`](../src/Wipelog.App/Services/LibraryWatcher.cs))
wraps a `FileSystemWatcher` on the configured output directory and
invokes `ClipListViewModel.RefreshAsync` when an mp4 or sidecar lands,
is renamed (the daemon's hidden `.NAME.recording.mp4` rename), or is
deleted. The refresh re-queries the library (which the daemon has just
written into), so the new clip appears in the table within a few ms of
finalize.

- `IncludeSubdirectories = false` — the daemon's `.preroll/` subdirectory
  is intentionally hidden from the watcher (and from
  `LibraryIndexer`, which scans `TopDirectoryOnly`).
- The watcher's refresh is debounced; multiple `Created`/`Renamed`
  events for the same clip collapse to one `RefreshAsync`.

When the user changes the output directory in Settings, the watcher is
disposed and rebuilt against the new path on save.

## Playback

`PlayerViewModel`
([`src/Wipelog.App/ViewModels/PlayerViewModel.cs`](../src/Wipelog.App/ViewModels/PlayerViewModel.cs))
owns a single shared `LibVLC` instance (injected, created once at
`App.OnFrameworkInitializationCompleted`) and a `MediaPlayer` it binds
to the `VideoView` in `PlayerView.axaml`. On clip selection it builds
a `Media` from the mp4 path and calls `MediaPlayer.Play`.

Key wiring:

- `PlayerViewModel.PositionMs` is the bound scrub position. The setter
  writes through to `MediaPlayer.Time`; the LibVLC `TimeChanged` event
  feeds it back. The same property drives the `ScrubBar` knob.
- Audio tracks are populated on `MediaPlayer.ESAdded` — LibVLC doesn't
  expose the track list until decoding starts, and the first entry is
  always a `"Mute"` pseudo-track at id `-1` that the VM filters out.
- Seeks during PvP content (Arena, Solo Shuffle) where the mp4 was
  finalised as `_complete.mp4` and later renamed to `_win.mp4` /
  `_loss.mp4` work because LibVLC opens the file fresh on each
  `Media`, not by inode.

## Timeline rendering

The scrub bar is `Controls/ScrubBar.cs` plus the `TombstoneStrip`
custom control stacked above it (`Controls/TombstoneStrip.cs`).
`TimelineViewModel`
([`src/Wipelog.App/ViewModels/TimelineViewModel.cs`](../src/Wipelog.App/ViewModels/TimelineViewModel.cs))
exposes:

- `ClipDurationMs` — **video** duration (encounter duration + pre-roll
  prefix if any), not encounter duration. The video file is the user's
  timebase.
- `PreRollMs` — the prefix length in ms; `ScrubBar` and
  `TombstoneStrip` add this to each `TimelineEvent.OffsetMs` before
  projecting to an X coordinate, so events line up with the underlying
  frame.
- `Events` — the deserialised `.events.json` content (`PlayerDeathEvent`,
  `BossStartEvent`).

When the user clicks a tombstone, the strip emits a seek target in
video-time (already pre-shifted). The death-rewind shortcut from
`PlayerView.OnTombstoneMarkerClicked` adds `PreRollMs` back in because
it bypasses the strip and honours the user's `SeekBackSeconds`.

The tombstone two-row stacking, `+N` overflow badge, and boss-bar
rendering all live in `TombstoneRowLayout.cs` and `BossLabelLayout.cs`
(pure layout math, separated from the Avalonia control for tests).

## Daemon liveness & control

| Concern | Service | Reads / writes |
|---|---|---|
| Heartbeat | `FileBasedDaemonStatusReader` ([`Services/FileBasedDaemonStatusReader.cs`](../src/Wipelog.App/Services/FileBasedDaemonStatusReader.cs)) | Polls `~/.cache/wipelog/status.json` every ~2 s. If the file is missing, stale, or unparseable, reports `Unreachable`. |
| Stop / Record commands | `DaemonControlClient` ([`Services/DaemonControlClient.cs`](../src/Wipelog.App/Services/DaemonControlClient.cs)) | Opens a fresh Unix-domain socket connection per call, sends one JSON request, reads one JSON response, closes. Returns `ControlCallResult { Status, State, Error }`. |
| Service install / restart | `SystemctlDaemonLifecycleController` ([`Services/SystemctlDaemonLifecycleController.cs`](../src/Wipelog.App/Services/SystemctlDaemonLifecycleController.cs)) and `SystemdServiceInstaller` ([`Services/SystemdServiceInstaller.cs`](../src/Wipelog.App/Services/SystemdServiceInstaller.cs)) | `systemctl --user` invocations to install / enable / restart / stop the `wipelog.service` unit. Unit file built by `SystemdUnitFileBuilder` from the daemon-config sub-object in `preferences.json`. |

The GUI degrades gracefully when the daemon isn't running. Playback,
filtering, and metadata editing all work against the library and disk;
the sidebar shows "Daemon not running" with an "Install daemon"
affordance that opens Settings → Service.

## Settings overlay

`SettingsView.axaml` is a full-window overlay (not a modal dialog).
Three tabs: **Service** (daemon binary, log/output paths, codec /
quality / framerate, audio tracks, pre-roll), **General** (close-to-tray,
notification toggles, test-recording duration), **Display** (theme,
timeline appearance).

A `StackPanel` inside each tab has `Margin="0,0,0,80"` so the content
clears the bottom toolbar (Save / Cancel) without forcing the user to
scroll past the last input. This is intentional padding, not dead
whitespace.

`SettingsViewModel.IsOnboardingNeeded` becomes true when
`DaemonConfig.LogDirectory` is empty; the view shows the amber welcome
banner across the top of the page with the WoW-log detection
affordance. CLAUDE.md's "First-run onboarding" section covers the
detector and the apply flow.

## Tray icon

`TrayIconHost`
([`Services/TrayIconHost.cs`](../src/Wipelog.App/Services/TrayIconHost.cs))
runs alongside the main window. Closing the main window hides it to
the tray (the app sets `ShutdownMode.OnExplicitShutdown` in
`App.axaml.cs:68` so the close button doesn't quit). The tray menu has
Show window / Stop recording / Quit. Quit and the menu commands route
through the same `IDaemonLifecycleController` the sidebar uses.

The tray icon changes colour when the daemon flips to `Recording`
(see `TrayIconImages.cs` for the two-state icon set).

## Window state

`WindowStateService`
([`Services/WindowStateService.cs`](../src/Wipelog.App/Services/WindowStateService.cs))
persists the window size, position, and maximised flag to
`~/.config/wipelog/window.json` on close, and restores it on next
launch. Defaults to centre + 1280x800 if no file exists.

## Keyboard navigation

Global hotkeys (Space for play/pause, J/L for skip, Delete for the
focused row, etc.) are wired in `MainWindow.axaml.cs` via tunnelled
`KeyDown`. Because Avalonia routes those to `KeyboardDevice.FocusedElement`
and the obvious focus targets (DataGrid, Play button) start empty or
disabled, the XAML includes an invisible 1x1 `KeyboardAnchor` button
at `MainWindow.axaml:26` to ensure something focusable exists on launch.

Full shortcut list lives in the README and is also shown in the
in-app Shortcuts dialog (`Views/ShortcutsDialog.axaml`).
