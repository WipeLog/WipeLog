# Daemon

`Wipelog.Daemon` is the always-on process: it tails the WoW combat log,
decides when to record, runs `gpu-screen-recorder` as a child process,
writes clip sidecars and DB rows, and serves status / commands to the
GUI. It is a single-instance, single-user, console-style app built on
`Microsoft.Extensions.Hosting`.

## Responsibilities

In order of dependency:

1. **Combat log tailing** — `LogTailer`
   ([`src/Wipelog.Core/CombatLog/LogTailer.cs`](../src/Wipelog.Core/CombatLog/LogTailer.cs))
   watches a directory or single file via `FileSystemWatcher`, tracks a
   byte offset, and pushes parsed `CombatLogEvent` records into a
   `Channel<CombatLogEvent>`. Survives WoW restarts: new log files are
   picked up via the directory watcher, replacements/truncations reset
   the offset.
2. **Encounter detection** — `EncounterDetector`
   ([`src/Wipelog.Core/Detection/EncounterDetector.cs`](../src/Wipelog.Core/Detection/EncounterDetector.cs))
   is a pure state machine consuming the event stream and emitting
   `StartRecording` / `StopRecording` commands for raids, M+, Arenas,
   Solo Shuffle, and Battlegrounds. Safety timeouts are evaluated
   against event timestamps, not wall clock.
3. **Recording control** — `RecorderController`
   ([`src/Wipelog.Core/Recording/RecorderController.cs`](../src/Wipelog.Core/Recording/RecorderController.cs))
   glues the detector to `GpuScreenRecorder`. It owns the active
   recording's identity buffer, the `RecordingEventBuffer` (kill-blow
   attribution at `UNIT_DIED` observation time), the pre-roll lifecycle,
   and the finalize step that writes both sidecars and inserts the DB
   row.
4. **Library writes** — `LibrarySink`
   ([`src/Wipelog.Daemon/LibrarySink.cs`](../src/Wipelog.Daemon/LibrarySink.cs))
   implements `IClipRecorderSink` and turns the controller's finalized
   `ClipRecord` into a row in `clips.db`.
5. **Heartbeat publishing** — `HeartbeatWriter`
   ([`src/Wipelog.Daemon/Status/HeartbeatWriter.cs`](../src/Wipelog.Daemon/Status/HeartbeatWriter.cs))
   rewrites `~/.cache/wipelog/status.json` every 2 s via atomic temp-file
   rename. State comes from `HeartbeatState` which is mutated by the
   lifecycle observer, the ACL monitor, and the controller.
6. **ACL monitoring** — `AdvancedLoggingMonitor`
   ([`src/Wipelog.Daemon/Logging/AdvancedLoggingMonitor.cs`](../src/Wipelog.Daemon/Logging/AdvancedLoggingMonitor.cs))
   reacts to `COMBAT_LOG_VERSION` lines: if `ADVANCED_LOG_ENABLED=0`, it
   fires a `notify-send` toast once and flips the heartbeat flag.
   ACL=1 silently clears the flag.
7. **Notifications** — `NotificationService` / `NotifySendInvoker`
   ([`src/Wipelog.Daemon/Notifications/`](../src/Wipelog.Daemon/Notifications/))
   spawn `notify-send` for "clip saved" and error toasts; gated by
   `preferences.json` (`notifyOnClipSaved`, `notifyOnErrors`) and the
   `--no-notifications` kill switch.
8. **Control socket** — `ControlServer`
   ([`src/Wipelog.Daemon/Control/ControlServer.cs`](../src/Wipelog.Daemon/Control/ControlServer.cs))
   listens on `$XDG_RUNTIME_DIR/wipelog/control.sock` for one-shot JSON
   commands from the GUI. Today: `stopRecording`, `startManualRecording`;
   anything else returns `ok=false` with a descriptive error.

DI wiring lives in [`src/Wipelog.Daemon/Program.cs`](../src/Wipelog.Daemon/Program.cs).

## CLI reference

### Live mode (no subcommand)

```
Wipelog.Daemon (--log-path <file> | --log-dir <dir> [--log-pattern <glob>])
               --output <dir>
               [--db-path <path>]
               [--player-name <name>]
               [--executable gpu-screen-recorder]
               [--window focused]
               [--framerate 60]
               [--codec h264]
               [--audio default_output [--audio app:spotify ...]]
               [--container mp4]
               [--video-quality medium|high|very_high|ultra]
               [--preroll-seconds <2..86400>]
               [--no-notifications]
               [--log-output-path <file>|off]
```

Required: `--output`, and exactly one of `--log-path` or `--log-dir`.
Mixing the two is rejected at startup ([`Program.cs:90-104`](../src/Wipelog.Daemon/Program.cs)).

| Flag | Default | Notes |
|---|---|---|
| `--log-path` | — | Single combat log file (rare; mostly for fixtures and tests). |
| `--log-dir` | — | Combat log directory; the tailer follows the newest file matching `--log-pattern`. |
| `--log-pattern` | `WoWCombatLog-*.txt` | Glob applied inside `--log-dir`. |
| `--output` | — | Where finished clips and sidecars land. Hidden `.preroll/` subdirectory used for pre-roll temp files; excluded from the indexer and library watcher. |
| `--db-path` | `~/.local/share/wipelog/clips.db` (`XDG_DATA_HOME` aware) | SQLite library path. |
| `--player-name` | — | Hint for the spec resolver when `AFFILIATION_MINE` and frequency fallback both fail (diacritic-folded match). |
| `--executable` | `gpu-screen-recorder` | Override the recorder binary path (e.g. for a custom build). |
| `--window` | `focused` | gpu-screen-recorder `-w` target. Window-name strings or X11 window IDs both work. |
| `--framerate` | `60` | gpu-screen-recorder `-f`. Validated 24-240 in the preferences path; not at the CLI. |
| `--codec` | `h264` | Passed through to gpu-screen-recorder. |
| `--audio` | one track of `default_output` if no flag given | Repeatable; one mp4 audio track per occurrence. Both `--audio X` and `--audio=X` parse. Pre-scanned in `Program.cs:62-81` because `AddCommandLine` collapses repeated keys. |
| `--container` | `mp4` | Output container. |
| `--video-quality` | `medium` | One of `medium`, `high`, `very_high`, `ultra`. |
| `--preroll-seconds` | unset (disabled) | Opt-in pre-encounter buffer. Range 2-86400 (gpu-screen-recorder `-r` range). When set, a second gpu-screen-recorder runs in replay-buffer mode; the controller flushes and concatenates it onto each clip. |
| `--no-notifications` | off | Bare flag (no `=value`). Disables all desktop notifications regardless of `preferences.json`. |
| `--log-output-path` | `$XDG_STATE_HOME/wipelog/daemon.log` (default `~/.local/state/wipelog/daemon.log`) | Path for the file-logger tee. Pass `off` to disable. |

### `index` subcommand

```
Wipelog.Daemon index --output <dir> [--db-path <path>]
```

Walks the output directory's `.json` sidecars and reconciles the SQLite
library with reality: adds rows for clips on disk that aren't indexed,
removes rows whose video file no longer exists, refreshes `has_events`
based on `.events.json` presence. Used at daemon startup, and runnable
by hand when the library and disk get out of sync (e.g. after manual
clip deletion outside the GUI). Returns exit 2 if any per-clip errors
occurred.

### `backfill-events` subcommand

```
Wipelog.Daemon backfill-events --output <dir> --log-dir <dir>
                               [--log-pattern WoWCombatLog-*.txt]
                               [--db-path <path>]
                               [--force]
```

Replays the original combat log for the time window of each clip that
lacks a `.events.json` sidecar, writing a fresh one. `--force` regenerates
existing sidecars too — needed once after upgrading past schema v2 to
populate the new `combatants` list into old clips. The indexer re-runs
automatically afterwards so `has_events` flips for the newly-written
files.

Clips whose log window has rotated away from the configured `--log-dir`
are skipped silently (counted as `skipped (no log data)`).

## Logging

Two destinations:

- **Stderr / journal** via the default `Microsoft.Extensions.Logging`
  console provider. `journalctl --user -u wipelog` shows this for
  systemd-managed installs.
- **Daemon log file** at `$XDG_STATE_HOME/wipelog/daemon.log` (default
  `~/.local/state/wipelog/daemon.log`), provided by `FileLoggerProvider`
  ([`src/Wipelog.Daemon/Logging/FileLoggerProvider.cs`](../src/Wipelog.Daemon/Logging/FileLoggerProvider.cs)).
  The user can `tail -f` this directly without going through journald.

Override or disable the file with `--log-output-path <file>` or
`--log-output-path off`.

## Status flags exposed to the GUI

The heartbeat snapshot ([`HeartbeatState.Snapshot`](../src/Wipelog.Daemon/Status/HeartbeatState.cs))
combines:

- `state` — `Idle` or `Recording`, owned by `DaemonLifecycleObserver`.
- `currentRecording` — populated on `OnStarted`, cleared on `OnFinalized`.
- `clipsRecordedThisSession` — incremented on every successful finalize.
- `lastError` — short string set when the controller catches a fatal-ish
  error; the GUI surfaces it as an error pill in the sidebar.
- `advancedLoggingDisabled` — set/cleared by `AdvancedLoggingMonitor` on
  each ACL transition.

The GUI's `DaemonStatusViewModel` maps these to sidebar UI; see
[`gui.md`](gui.md).

## Development workflow

### Build & test

From the repo root:

```sh
dotnet build
dotnet test                    # runs Wipelog.Core.Tests
```

xUnit v3 on Microsoft.Testing.Platform — the usual
`--filter "FullyQualifiedName~..."` syntax does not apply. CLAUDE.md's
"Commands" section has the binary-direct filter syntax for running a
single test class or method.

### Run by hand against fixture logs

```sh
dotnet run --project src/Wipelog.Daemon -- \
  --log-path tests/Wipelog.Core.Tests/Fixtures/sample-events.txt \
  --output /tmp/wipelog-dev \
  --executable tests/Wipelog.Core.Tests/Fixtures/fake-gpu-screen-recorder.sh \
  --no-notifications
```

The fake recorder scripts in `tests/Wipelog.Core.Tests/Fixtures/` write a
short white-screen mp4 instead of capturing the screen, so you can
exercise the full pipeline without WoW running. `--no-notifications`
suppresses toast pop-ups while iterating.

To produce a backdated clip for date-range filter testing, use
`scripts/make-test-clip.sh --started-at <iso>` (writes a fake mp4 +
schema-v2 sidecar + `.events.json` directly).

### Redeploy after daemon changes

For a framework-dependent install (no .NET runtime bundled, faster to
iterate on) publish into `~/bin/wipelog`:

```sh
dotnet publish src/Wipelog.Daemon -c Release --no-self-contained \
  -o ~/bin/wipelog
dotnet publish src/Wipelog.App    -c Release --no-self-contained \
  -o ~/bin/wipelog-app
```

For systemd users, `systemctl --user restart wipelog` picks up the new
binary.

### Watch the live daemon

```sh
tail -f ~/.local/state/wipelog/daemon.log    # file-logger tee
journalctl --user -u wipelog -f              # systemd unit only

cat ~/.cache/wipelog/status.json | jq .      # current heartbeat snapshot
ls -la $XDG_RUNTIME_DIR/wipelog/             # socket present iff daemon is up
```

A quick "is the daemon doing anything" probe is to `cat` the status
file: `heartbeatAt` advances every 2 s, `state` flips to `"Recording"`
when an encounter starts.
