# Audit: `Wipelog.Daemon`

> **Note.** This is a snapshot from a per-area code-review pass.
> Specific line numbers and references to commit hashes may not match
> the current tree — `git log` and `git blame` are authoritative.

## Summary

The daemon is a thin wiring layer over Core + Library plus a handful of
process-level concerns (heartbeat, control socket, notifications, ACL
monitor, file-based logging). It's small (≈ 1,000 LOC across 16 files)
and stays that way. Two real concerns: (1) `Program.cs`'s
custom CLI argument pre-scanning is fragile, and (2) `LibrarySink` — the
boundary between in-process recording state and the persisted DB row —
has no direct test coverage.

## Critical findings

No critical findings.

## Important findings

### `Program.cs` argument pre-scanning is brittle

[`src/Wipelog.Daemon/Program.cs:54-81`](../src/Wipelog.Daemon/Program.cs)
manually pre-scans `args` to handle two flag shapes the host builder's
`AddCommandLine` can't express:

- `--no-notifications` is presence-only (no `=value`).
- `--audio X` and `--audio=X` are repeatable, and `AddCommandLine`
  collapses repeats into one value.

The pre-scan loops index the array by hand
(`for (int i = 0; i < args.Length; i++)`, increment `i` when consuming
the next token). It works today, but any new flag that needs either
treatment would mean another loop with the same pattern, and the
ordering between the two pre-scans matters (a malformed
`--audio` at the end of the args would unbalance the index).

**Why it matters.** The daemon CLI is a stable public surface (the
systemd unit and any wrapper script depend on it). Argument parsing bugs
are the kind that pass tests and break a real user's invocation. The
loops have no direct tests.

**Suggested direction.** Either lean on `System.CommandLine`
(now stable in .NET 10) for the full argument surface, or pull the
pre-scan into a single helper with a parameter for "presence-only
flag names" and "repeatable flag names" and unit-test it. Don't add a
third special case to the inline loops.

### `LibrarySink` is untested

[`src/Wipelog.Daemon/LibrarySink.cs`](../src/Wipelog.Daemon/LibrarySink.cs)
is the only mapping from `ClipFinalization` to the persisted
`ClipRecord`. It hard-codes which fields are carried over and which
are computed (e.g. `Difficulty` is set only for `RaidEncounter`,
`HasEvents = clip.EventsSidecarPath is not null`). No test exercises
it.

**Why it matters.** Bugs in this mapping silently produce
half-populated DB rows. The PvP changes in stages 6 and 7 added
`ArenaBracket`, `BossHpPercent`, and `KeyUpgradeLevel` to the record;
each one needed a line edited here, and there is no test that would
have caught one being missed.

**Suggested direction.** A small `LibrarySinkTests` with an
in-memory `IClipRepository` capturing the `ClipRecord` it gets handed.
One test per content type, asserting which fields are populated.

### Empty catches in `HeartbeatWriter` shutdown path

[`src/Wipelog.Daemon/Status/HeartbeatWriter.cs:121`](../src/Wipelog.Daemon/Status/HeartbeatWriter.cs)
has `try { ... } catch { /* best effort */ }` — empty catch. It runs
in the writer's temp-file cleanup when the rename failed. The intent
is fine ("don't crash shutdown trying to delete a tmp file"), but a
silent catch with no log line removes the only signal that something
is wrong with the status path.

**Why it matters.** If `XDG_CACHE_HOME` is pointing somewhere the
daemon can't write — exactly the situation
[`troubleshooting.md`](troubleshooting.md) flags — this path keeps
firing every 2 seconds for the daemon's lifetime, with zero log
output to indicate the failure. The user's experience is "daemon is
running but GUI says it isn't."

**Suggested direction.** Log at `LogTrace` or `LogDebug` even for
"best effort" cleanup, so a developer with verbose logging on can
see the path failing. Same change applies to the two empty catches
in `ControlServer.cs:101-103` (socket close + unlink on shutdown).

## Nice-to-have findings

### `FileLoggerProvider` has no tests

The custom file logger
([`src/Wipelog.Daemon/Logging/FileLoggerProvider.cs`](../src/Wipelog.Daemon/Logging/FileLoggerProvider.cs))
is a small implementation but a stop-loss for "what happened in the
daemon when it crashed" — users without systemd debugging chops rely
on `~/.local/state/wipelog/daemon.log` as their only window. A
round-trip test that writes a few log entries and reads them back
would catch the next refactor from breaking it.

### `Program.cs` mixes config parsing with DI wiring

The first ~210 lines of `Program.cs` are argument parsing; the next
~100 are DI registration; the bottom is two subcommand handlers. The
argument-parsing block could move to a `DaemonOptions.Parse(string[])`
static method (matching the existing `DaemonOptions` record), leaving
`Program.cs` to be only DI + lifecycle. Trivial; flagged because
testing argument parsing in isolation is currently impossible.

## Observations (not findings)

### Single-instance daemon model

`ControlServer` unlinks any pre-existing socket file at startup
([`ControlServer.cs:64`](../src/Wipelog.Daemon/Control/ControlServer.cs))
on the assumption that no other daemon is running. Two daemons against
the same `XDG_RUNTIME_DIR` would step on each other (the second one
would unlink the first's live socket and bind its own). The systemd
unit enforces single-instance via the unit definition, but a user
running the daemon manually and then `systemctl --user start wipelog`
in the same session has two daemons.

Is this intentional? If yes, document it (a one-line comment at the
unlink would say "only one daemon per user session — see packaging/").
If not, an `fcntl` lock file is the standard solution.

### The pre-roll directory layout depends on a `.` prefix

The pre-roll snippets live in `<output>/.preroll/` and that dotted
prefix is what keeps them invisible to `LibraryIndexer` (which scans
`TopDirectoryOnly`) and `LibraryWatcher` (`IncludeSubdirectories =
false`). It's documented in CLAUDE.md but not in any code comment;
renaming the directory would silently break the invariant.

A one-line constant — `private const string PreRollSubdir = ".preroll";`
— either in `RecorderController` or `DaemonOptions` would make the
intent explicit. Not urgent; raised so an audit reader knows the
convention isn't enforced anywhere but the path string.

## Tests

### Coverage by area

| Area | Tests |
|---|---|
| Control socket | `ControlServerTests` — end-to-end via a real local Unix socket; covers `stopRecording`, `startManualRecording`, unknown command, idle / recording state transitions. |
| Heartbeat | `HeartbeatWriterTests` — JSON shape, atomic rename, file cleanup on stop. |
| Backfill | `EventsBackfillerTests` — replays the fixture log, asserts written sidecars. |
| ACL monitor | `AdvancedLoggingMonitorTests` (in Core's Recording-aware test suite). |
| Notifications | `NotificationServiceTests` — gated dispatch through `INotifySendInvoker`. |
| Systemd integration | `SystemdServiceInstallerTests`, `SystemdUnitFileBuilderTests`, `SystemdUnitFileParserTests` — these technically live under App but exercise the daemon-config flow. |

### Gaps

- **`LibrarySink`** — see "Important findings" above.
- **`Program.cs` argument parser** — see above. The pre-scan code paths
  are exercised only by running the daemon end-to-end.
- **`FileLoggerProvider`** — see "Nice-to-have findings."
- **`HeartbeatState`** state transitions — covered indirectly through
  `HeartbeatWriterTests`, but no direct test asserts that a `Recording`
  transition + `OnError` produces the expected snapshot. Today the
  invariant is "Snapshot is consistent regardless of mutation order";
  no test pins that down.

### Quality

The existing tests are good: `ControlServerTests` opens a real socket
on a temp path rather than mocking. `AdvancedLoggingMonitorTests`
asserts both the state-transition contract and the notify-send call
count (so accidental double-notify regressions are caught). No
brittleness flags.
