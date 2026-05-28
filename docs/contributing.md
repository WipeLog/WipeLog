# Contributing

This document is about how to develop *on* Wipelog. For end-user install
and usage, see the [project README](../README.md); for system-level
architecture, [`architecture.md`](architecture.md); for daemon
specifics, [`daemon.md`](daemon.md).

## Prerequisites

- **.NET SDK 10.0.104+** — pinned in [`global.json`](../global.json)
  with `rollForward: latestFeature`, so any 10.0.x ≥ .104 works.
- A Linux box with X11 (gpu-screen-recorder support — Wayland is not
  currently exercised, see [`troubleshooting.md`](troubleshooting.md)).
- `gpu-screen-recorder` on `$PATH` if you want to do live recording.
  Tests don't need it — fixtures ship fake shell scripts that emit a
  blank mp4.
- `notify-send` (libnotify) if you want desktop notifications. Optional;
  the daemon survives without it.

## Project layout

```
src/
  Wipelog.Core/      pure logic — combat-log parsing, tailer, encounter
                     detection, recorder subprocess wrapper, recording
                     event buffer, timeline sidecar shape. No DB, no UI.
  Wipelog.Library/   SQLite (Dapper) clip repository, migrations, indexer,
                     bulk operations. Depends on Core for ContentType and
                     PlayerIdentity only.
  Wipelog.Daemon/    Microsoft.Extensions.Hosting console app. Wires
                     LogTailer → EncounterDetector → RecorderController
                     → LibrarySink. Owns the heartbeat writer, the
                     control-socket server, and the notification dispatch.
  Wipelog.App/       Avalonia 11 GUI. Depends on Library and Core.
                     CommunityToolkit.Mvvm view models, LibVLCSharp for
                     playback.
tests/
  Wipelog.Core.Tests/   xUnit v3 on Microsoft.Testing.Platform. The only
                        test project; references all four src projects
                        so tests for Library / Daemon / App live here too.
```

The dependency direction is strict: nothing in Core knows about Library
or UI; Library only sees Core; Daemon and App both depend on Core +
Library. New code should respect this — UI helpers don't belong in Core,
and DB code doesn't belong in Daemon.

## Build

```sh
dotnet build              # all four projects + tests
```

`Directory.Build.props` sets `TreatWarningsAsErrors=true` and
`EnforceCodeStyleInBuild=true`, so warnings fail the build. Fix them
where they appear; don't suppress per-file unless there's a documented
reason (e.g. `Tmds.DBus.Protocol` NU1903 in `Wipelog.App.csproj`).

## Test

```sh
dotnet test               # full suite
```

xUnit v3 + Microsoft.Testing.Platform (`<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`).
The traditional `dotnet test --filter "FullyQualifiedName~Foo"` syntax
does **not** apply. To run a subset, build first and invoke the test
binary directly with the MTP filter flags:

```sh
cd tests/Wipelog.Core.Tests/bin/Debug/net10.0
./Wipelog.Core.Tests \
    --filter-class "Wipelog.Core.Tests.PlayerSpecResolverTests" \
    --filter-method "*Mine*"
```

Wildcards are allowed at the start and end of `--filter-method`.

### Test conventions

- Tests use `TestContext.Current.CancellationToken` instead of
  `xunit.NET.Test.CancellationToken`.
- The standard scratch-dir helper is `TempDirectory`; the standard
  test clock is `FakeClock` (not `FixedClock`).
- Fixture files live under
  [`tests/Wipelog.Core.Tests/Fixtures/`](../tests/Wipelog.Core.Tests/Fixtures/).
  When adding a new fixture, also add it to the csproj's `<None Update>`
  list with `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`
  or the file won't reach the test binary's working directory.
- The daemon project exposes internals to the test project via
  `<InternalsVisibleTo>Wipelog.Core.Tests</InternalsVisibleTo>`. Tests
  for `internal` types (e.g. `EventsBackfiller`,
  `AdvancedLoggingMonitor`) are valid; don't make a type `public` just
  to write a test against it.

The only test project — `Wipelog.Core.Tests` — references all four
production projects. This keeps the friction low for tests that cross
boundaries (e.g. `RecorderControllerTests` which exercises Core +
fakes), at the cost of mixing unit and integration concerns in one
project. See [`docs/audit-core.md`](audit-core.md) for the discussion.

## Publish

Two paths, both supported:

### Self-contained single-file binaries

```sh
./publish.sh
```

Produces `dist/daemon/Wipelog.Daemon` and `dist/app/Wipelog.App` —
linux-x64, single-file, native libraries (`libSkiaSharp.so`,
SQLite, etc.) bundled and self-extracted at startup. Use this for the
GitHub release artefacts and for users who don't have .NET installed.

### Framework-dependent layout (developer / packager loop)

```sh
dotnet publish src/Wipelog.Daemon -c Release --no-self-contained \
  -o ~/bin/wipelog
dotnet publish src/Wipelog.App    -c Release --no-self-contained \
  -o ~/bin/wipelog-app
```

This needs `dotnet` on the host but is much smaller than the
self-contained build and faster to iterate on. The systemd unit
shipped under [`packaging/`](../packaging/) drives the same binaries;
see [`packaging/README.md`](../packaging/README.md).

## Commit conventions

Read the last 40 commits for the running shape — these are the
patterns to match:

- **Conventional commits** with subsystem scope. Format:
  `type(scope): summary`. Examples observed:
  - `feat(app): first-run onboarding banner with WoW log detection`
  - `fix(recording): auto-stop M+ recording on zone change out of dungeon`
  - `feat(core+app): resolve Arena win/loss at finalize from faction signals`
  - `docs(claude.md): document the clip notes editor and MM:SS chip strip`
  - `chore: rename WowRecorder → Wipelog`
- **Type vocabulary**: `feat`, `fix`, `docs`, `chore`, `test`. No
  `refactor` / `perf` / `style` observed — they fold into `chore` or
  one of the other categories.
- **Scope vocabulary**: `app`, `daemon`, `core`, `library`, `recording`,
  `audio`, `settings`, `scripts`, `packaging`, `claude.md`. Multiple
  scopes joined with `+`: `core+app`, `app+packaging`.
- **Summary**: lowercase, present tense, specific. State the user-visible
  outcome where you can ("auto-stop M+ recording on zone change out of
  dungeon" beats "fix M+ detection").

For longer commits, an explanatory body is welcome — read commits like
`71e509c feat(recording): pre-encounter buffer …` for examples of how
the body relates to the summary.

## Where each kind of change belongs

- **A new combat-log event** — parser in
  `src/Wipelog.Core/CombatLog/CombatLogParser.cs`, event record in the
  same directory, fixture under
  `tests/Wipelog.Core.Tests/Fixtures/`, test in
  `tests/Wipelog.Core.Tests/CombatLogParserTests.cs`.
- **A new encounter / content type** — extend `ContentType` in
  `src/Wipelog.Core/Detection/RecordingCommand.cs`, detection logic in
  `EncounterDetector.cs`, sidecar shape in
  `src/Wipelog.Core/Recording/ClipSidecar.cs`, GUI plumbing in
  `Wipelog.App/ViewModels/ClipFiltersViewModel.cs` and
  `ClipDetailsViewModel.cs`, and a sidebar tab in `SideNavViewModel.cs`.
- **A new daemon CLI flag** — parse in
  `src/Wipelog.Daemon/Program.cs`, plumb through `DaemonOptions`, hand
  to the relevant service via DI.
- **A schema change** — new SQL file in
  `src/Wipelog.Library/Migrations/0NN_short_name.sql` (last statement
  must be `PRAGMA user_version = N;`), bump `CurrentSchemaVersion` in
  [`ClipDatabase.cs`](../src/Wipelog.Library/ClipDatabase.cs), update
  the repository methods, document in
  [`data-formats.md`](data-formats.md).
- **A new view / view model** — paired `*.axaml` in `Views/` and a
  view model in `ViewModels/`. Inherit `ViewModelBase`. Wire into
  `MainWindowViewModel` if it's a top-level pane, or into the parent
  VM if it's a child component.

## Project conventions worth observing

These are inferred from the existing code; CLAUDE.md is the
authoritative reference for the why behind each one.

- **GPL-3.0 header on every .cs file.** Copy from any existing source
  file; the project's user memory notes that the header still says
  "WowRecorder" and should be left alone post-rename.
- **ASCII-only typography** in source and docs: use `-` not en-dash
  (U+2013), `x` not multiplication sign (U+00D7). Em-dash (U+2014) is
  fine. The repo has commits (`7180f9b`, `09f9dbb`) that exist
  specifically to enforce this.
- **Cancellation tokens** propagate through every async method that
  spans a unit of work — pass them, accept them, don't swallow them.
- **`Async` suffix** on async methods.
- **Logging** through `ILogger<T>` in constructor; no static loggers,
  no `Console.WriteLine` in production code paths.
- **Repository pattern** for Library (`IClipRepository` is the
  abstraction, `ClipRepository` the Dapper-backed implementation).
- **`x:DataType` on every Avalonia view** for compiled bindings.

## What stays out of source control

- Generated build output (`bin/`, `obj/`) — covered by
  [`.gitignore`](../.gitignore).
- The self-contained `dist/` output of `publish.sh`.
- The `screenshots/` directory (excluded — used for screenshots that
  go into README / GitHub release notes but aren't reproducible).
- Personal config files (window state, preferences) — those live under
  `~/.config/wipelog/` and `~/.cache/wipelog/`, not in the tree.
