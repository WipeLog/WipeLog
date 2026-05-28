# Audit: `Wipelog.App`

> **Note.** This is a snapshot from a per-area code-review pass.
> Specific line numbers and references to commit hashes may not match
> the current tree — `git log` and `git blame` are authoritative.

## Summary

The GUI is the largest project (≈ 8,600 LOC across 57 files) and the
most varied: 16 view models, 11 XAML views with code-behinds, 20
services, 8 custom controls. The MVVM separation is mostly clean, the
custom controls have their layout math broken out into testable
classes, and the LibVLC + DataGrid integrations stay shallow. The
biggest specific concern is `SettingsViewModel` at 931 lines; the
broader concern is that the App test surface is the smallest relative
to its size.

## Critical findings

No critical findings.

## Important findings

### `SettingsViewModel.cs` at 931 lines is doing too much

[`src/Wipelog.App/ViewModels/SettingsViewModel.cs`](../src/Wipelog.App/ViewModels/SettingsViewModel.cs)
carries fifteen commands (install / uninstall daemon, restart / stop /
start, save preferences, detect WoW logs, apply detected path,
reindex, test recording, open directory, etc.), at least nine
distinct broad catch sites, and the state machine for the install
workflow. It owns the wiring between four services
(`AppPreferencesService`, `UserPreferencesService`, `IServiceInstaller`,
`IWowLogDetector`), the indexer, the clip list, the player, and the
daemon-status VM.

**Why it matters.** The Settings view is where most of the unfamiliar
work happens for a new user, and it's also the place where bugs are
most expensive (a misconfigured systemd unit doesn't run, a
miswritten preferences file silently wipes config). Nine catches in
one VM is a lot of "best effort" boundaries; some of them shadow
errors the user would benefit from seeing as a status-message banner
instead of a swallowed log line.

**Suggested direction.** A natural split: the daemon
install/uninstall/restart commands and their workflow VM could become
a `DaemonInstallViewModel` consumed by the Settings view's Service
tab. Same for the detect-WoW-logs flow (already isolated to its own
detector service; the VM glue is a few hundred lines). That's the
shape `EditClipMetadataDialogViewModel` and `TagsEditorViewModel`
already take — co-located VM-per-flow.

### Avalonia App tests skip the platform layer entirely

`ClipListViewModelTests`, `SettingsViewModel*`-style tests (via the
detector/installer mocks), and `DaemonStatusViewModelTests` exercise
the view models, but no test renders a view or boots Avalonia.
Brittleness in actual XAML bindings (typos in property paths,
unresolved `x:DataType` references, `IsVisible` triggers binding
against nullable types) goes unnoticed until a real `dotnet run`.

**Why it matters.** The codebase uses compiled bindings with
`x:DataType` precisely so the compiler catches these — but the bindings
are still evaluated lazily at view-load time. The recent
`f4f1dea fix(app): suppress LibVLC window flash on startup` and
`c77a3af fix(app): give Avalonia an initial focus owner so global
hotkeys work on launch` are exactly the class of bug that a headless
view test would catch on PR rather than after manual smoke.

**Suggested direction.** Avalonia.Headless.XUnit (already on the
solution's `Wipelog.Core.Tests` dependency tree through
`Avalonia.Desktop`) lets you spin a real `MainWindow` against a
fake renderer. One smoke test per top-level view that asserts "it
loads, no binding errors logged" would close the gap with low
maintenance cost.

### `MainWindow.axaml.cs` global key handlers and the focus anchor

The keyboard-anchor button at
[`MainWindow.axaml:26-30`](../src/Wipelog.App/Views/MainWindow.axaml)
exists because Avalonia routes tunnel `KeyDown` to whatever has
focus, and the obvious targets (DataGrid, Play button) start empty
or disabled. This works, but the workaround is a magic 1x1 invisible
control whose role is invisible from the XAML itself.

**Why it matters.** A later developer reflowing the layout will
almost certainly remove the `KeyboardAnchor` thinking it's
left-over scaffolding, then spend a confusing afternoon debugging
why Space-to-play no longer works.

**Suggested direction.** The XAML comment is already there
(`MainWindow.axaml:20-25`). What would help more: assert the focus
contract in a `MainWindow.axaml.cs` `OnAttachedToVisualTree`
override that explicitly calls `KeyboardAnchor.Focus()` and logs at
debug. Even a one-line comment in the code-behind tying it back to
the XAML comment would make the cross-file dependency visible.

## Nice-to-have findings

### `Tmds.DBus.Protocol` NU1903 suppression is acknowledged

[`src/Wipelog.App/Wipelog.App.csproj:7-10`](../src/Wipelog.App/Wipelog.App.csproj)
suppresses NU1903 with a code comment explaining that the
vulnerability is in transitive `Tmds.DBus.Protocol` used for libnotify
outbound only — the App never reads inbound DBus messages, so the
exploitable surface isn't present. This is the right amount of
documentation; flagged only so a future reviewer doesn't strip the
comment along with the `<NoWarn>` line. (CLAUDE.md does not currently
mention this; consider a line under "Common pitfalls".)

### `Converters.cs` is a grab-bag

[`src/Wipelog.App/ViewModels/Converters.cs`](../src/Wipelog.App/ViewModels/Converters.cs)
collects every Avalonia `IValueConverter` in one file. That works until
two converters need the same helper, at which point splitting becomes
awkward. Today there's no actual pain; flagged because the file is on
a trajectory and the natural-feeling split (per-target-type
subdirectory `Converters/`) is cheap to do early.

## Observations (not findings)

### `async void` in `ClipsTableView.axaml.cs:204, 221` is correct

`OnContextCopyPath` and `OnContextEditMetadata` are both bound to
`MenuItem.Click` (a routed event) in the context menu — see
[`ClipsTableView.axaml:159-164`](../src/Wipelog.App/Views/ClipsTableView.axaml).
`async void` on an event handler is the conventional and correct
pattern; flagged here because a casual code review (or an over-eager
audit) would mark these as a bug. They aren't.

### MVVM is mostly clean, with a few `App.Current.Services` lookups

`ClipsTableView.axaml.cs` reaches into `App.Current.Services` to fetch
`PlayerViewModel`, `IClipRepository`, and `ClipListViewModel` instead
of receiving them via constructor injection. This is normal for an
Avalonia view code-behind (XAML views can't take constructor params
the same way VMs can), but it does mean the wiring is invisible from
the VM layer.

Is this intentional? If yes, a one-line comment near the call sites
("we fetch services from `App.Current` because XAML constructs us
parameterlessly") would prevent the next developer from "fixing" it
to a ctor injection that doesn't exist. If not, the standard solution
is to bind the relevant commands to the view's DataContext VM so the
code-behind doesn't need to know.

### View models do not share a base class beyond `ViewModelBase`

`ViewModelBase` is `CommunityToolkit.Mvvm.ComponentModel.ObservableObject`
plus the GPL header. There's no Wipelog-specific shared behaviour
(common error-message-flashing, common busy-state property, common
disposal pattern). Each VM rolls its own `IsWorking` / `StatusMessage`
where it needs one (`SettingsViewModel.IsWorking`,
`ClipListViewModel.IsLoading`, etc.). The names drift.

Is the convergence worth pursuing? Probably not unless / until a new
VM lands and has to invent the wheel again — but if you ever do, the
delta is small and naming becomes consistent.

### Settings overlay vs modal dialog

The Settings view is a full-window overlay
([`MainWindow.axaml:69`](../src/Wipelog.App/Views/MainWindow.axaml)),
not a modal dialog. That's a deliberate choice — playback continues
behind the overlay if the user had a clip loaded. It's worth knowing
this is intentional before "fixing" it to a `Window.ShowDialog`.
[`gui.md`](gui.md) calls this out; CLAUDE.md doesn't (yet).

## Tests

### Coverage by area

| Area | Tests |
|---|---|
| Filter / list VMs | `ClipListViewModelTests`, `ClipListItemViewModelTests`, `ClipFiltersViewModelTests`, `ClipsTableColumnSetTests`. |
| Details / dialogs | `ClipDetailsViewModelTests`, `EditClipMetadataDialogViewModelTests`, `TagsEditorViewModelTests`. |
| Status + side nav | `DaemonStatusViewModelTests`, `SideNavViewModelTests`, `DisplayOptionsViewModelTests`. |
| Services | `ClipArchiverTests`, `ClipEventsLoaderTests`, `FileBasedDaemonStatusReaderTests`, `WowLogDetectorTests`, `TrayIconHostTests`, `SystemdServiceInstallerTests`, `SystemdUnitFileBuilderTests`, `SystemdUnitFileParserTests`, `UserPreferencesServiceTests`. |
| Custom controls (layout math) | `BossLabelLayoutTests`, `TombstoneRowLayoutTests`, `TimelineHitTestTests`, `TimelineTooltipFormatterTests`. |
| Formatters | `ClipPovFormatterTests`. |

### Gaps

- **`SettingsViewModel`** itself: no test class. The detector mock and
  installer mock could drive most of the workflow VM scenarios; the
  install / uninstall / restart sequence in particular has worth
  asserting (which side effects fire in which state).
- **`PlayerViewModel`** has no test. LibVLC bindings aren't trivial to
  exercise, but the seek logic (`PositionMs` <-> `MediaPlayer.Time`
  round-trip, debouncing) and the audio-track filter
  (drop pseudo-mute id `-1`) are pure code paths that could be unit-tested
  with a fake `MediaPlayer` wrapper.
- **No headless-view smoke tests** — see the matching "Important"
  finding.
- **`LibraryWatcher`** is exercised indirectly through end-to-end
  scenarios. A direct test of the debounce / coalescing behaviour
  would catch regressions in the "many `Created` events from a
  single clip" path.

### Quality

The custom-controls layout tests (`BossLabelLayoutTests`,
`TombstoneRowLayoutTests`, `TimelineHitTestTests`) are model citizens
— the layout math lives in pure helpers and the tests assert
against deterministic outputs without touching Avalonia. That
separation is the right shape for any new control work.

The service tests (`SystemdServiceInstallerTests` in particular) read
back generated unit files and parse them with the same parser used at
runtime — round-trip coverage with no mocking.

No brittleness or slow-test flags.
