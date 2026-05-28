# Troubleshooting

Known gotchas and the workarounds the project has settled on. If you
hit one of these, the section explains the symptom, the cause, and
what the code does today.

For end-user-facing problems (recording doesn't start, GUI can't see
clips), the project [README](../README.md) has a higher-level
troubleshooting block. This document is about the implementation
quirks a developer will run into.

## LibVLC opens its own window briefly at app startup

**Symptom.** Before the Avalonia main window appears, a separate empty
window flashes on screen for one or two frames.

**Cause.** LibVLC's default video output opens its own X11 window
before the `VideoView` control attaches to it.

**Workaround.** `AppServices.Build` passes three init arguments to
`LibVLC` (see [`src/Wipelog.App/AppServices.cs:47-52`](../src/Wipelog.App/AppServices.cs)):
`--no-video-title-show`, `--no-snapshot-preview`, and `--vout=none`.
`--vout=none` is the critical one — `VideoView` re-enables a real
output when it binds. Don't drop these without confirming the flash
doesn't return.

## Settings tab content gets clipped at the bottom on 1440p

**Symptom.** The bottom inputs in a Settings tab disappear behind the
Save / Cancel toolbar on smaller / 1440p-aspect screens.

**Cause.** The Settings overlay is a single Grid with no scroll-padding
at the bottom of each tab's content `StackPanel`.

**Workaround.** Each tab's content `StackPanel` in
[`Views/SettingsView.axaml`](../src/Wipelog.App/Views/SettingsView.axaml)
has `Margin="0,0,0,80"` (see lines 200, 247, 417, 476). The matching
commit is `3b6a981 fix(settings): add bottom padding so all sections
are reachable on 1440p`. Keep the 80 px when adding new Settings
sections.

## Combat-log file rotation across WoW restarts

**Symptom.** After WoW is restarted, the daemon stops emitting events
even though a new `WoWCombatLog-*.txt` file has been created in the
configured log directory.

**Cause.** WoW writes a fresh log file per session; the tailer needs to
notice the new file appearing in the directory rather than continuing
to read the stale one.

**Workaround.** `LogTailer`
([`src/Wipelog.Core/CombatLog/LogTailer.cs`](../src/Wipelog.Core/CombatLog/LogTailer.cs))
runs in directory mode when `--log-dir` is set: a `FileSystemWatcher`
on the directory triggers a re-scan when the matching newest file
changes. Use `--log-dir`, not `--log-path`, for any real install —
single-file mode is for fixtures.

## Kill-blow attribution gap on periodic damage

**Symptom.** Players who die from periodic damage (DoTs, environment
ticks) show up on the timeline as deaths with `killedBySource` and
`killedBySpell` null. Historically this happened intermittently for
direct damage too.

**Cause.** The `RecordingEventBuffer` evicts old damage events on every
add. If kill-blow attribution had run at `Build()` time (final assembly
of the timeline), the relevant damage record might already have been
evicted by the time `UNIT_DIED` was processed.

**Workaround.** Attribution runs at `Observe(UnitDied)` time, not at
`Build()` — the controller captures the killing blow the moment a
death event is observed. See commits
`4ca988c fix: attribute kill blow at Observe(UnitDied) time, not at
Build` and `f80f6f4 test: failing test exposes kill-blow eviction-vs-Build
timing bug`. If you move attribution back into `Build`, eviction will
silently wipe the data again. CLAUDE.md's "Common pitfalls" section
flags this explicitly.

For clips recorded before this fix landed, the daemon subcommand
`Wipelog.Daemon backfill-events --force` regenerates the sidecar from
the raw log.

## Non-ASCII names rendered as `å` in sidecar JSON

**Symptom.** A character name like `Frostytånegl` shows up in
`.json` as `Frostytånegl`. Functionally fine; visually ugly.

**Cause.** `System.Text.Json` defaults to the strict HTML-safe encoder,
which escapes anything outside ASCII for context-safe output.

**Workaround.** Every sidecar writer uses
`JavaScriptEncoder.UnsafeRelaxedJsonEscaping`
([`ClipSidecar.cs:67`](../src/Wipelog.Core/Recording/ClipSidecar.cs),
[`TimelineEvents.cs:122`](../src/Wipelog.Core/Recording/TimelineEvents.cs),
[`HeartbeatWriter.cs:32`](../src/Wipelog.Daemon/Status/HeartbeatWriter.cs)).
The "unsafe" name refers to HTML-context use; for filesystem JSON it is
the right default. Read paths don't care — both encoders deserialise
either form.

If you add a new JSON writer, copy these options or names will regress
in your output.

## gpu-screen-recorder window-vs-display capture

**Symptom.** When `--window focused` captures a borderless-windowed
WoW client, the resulting mp4 sometimes captures the wrong window
(taskbar / notification popup that briefly took focus).

**Cause.** `gpu-screen-recorder -w focused` snapshots whatever has
keyboard focus at the moment it starts. If WoW just lost focus to
another window, you get that window instead.

**Workaround.** None inside Wipelog; this is a `gpu-screen-recorder`
constraint. Users can pass a specific window ID with `--window <id>`
or a display name (e.g. `--window DP-1`) instead. The Settings →
Service tab lists detected monitors so the user can pick a display
target. CLAUDE.md notes this constraint indirectly under the daemon
CLI.

## X11 vs Wayland

**Symptom.** `gpu-screen-recorder` reports a Wayland environment and
either fails to start or produces an empty file.

**Cause.** `gpu-screen-recorder`'s Wayland support exists but is
narrower than its X11 path. Wipelog has not been exercised against
Wayland sessions; the recorder process invocation
(`src/Wipelog.Core/Recording/GpuScreenRecorder.cs`) doesn't gate on
`XDG_SESSION_TYPE`.

**Workaround.** Use an X11 (or XWayland) session for now. A real
Wayland mode would mean (a) verifying gpu-screen-recorder Wayland
output, (b) confirming the `--window` flag's behaviour against
Wayland surfaces, and (c) testing the focused-app detection used to
auto-disable recording during alt-tabs. Treat Wayland as deferred,
not unsupported in principle.

## "Daemon not running" with the daemon actually running

**Symptom.** The sidebar shows the daemon as down, but `pgrep
Wipelog.Daemon` finds the process.

**Cause.** The GUI polls
`$XDG_CACHE_HOME/wipelog/status.json` (default `~/.cache/wipelog/`)
and treats `heartbeatAt` older than 30 s as `Unreachable`. If
`$XDG_CACHE_HOME` differs between the user's interactive shell and
the systemd-managed daemon (or if the daemon's filesystem is read-only
under a sandbox), the heartbeat never lands where the GUI looks.

**Workaround.** Confirm `~/.cache/wipelog/status.json` exists and is
recent. If the daemon prints `Failed to write heartbeat at <path>`
in its log, it's a filesystem permission issue. The systemd unit
should run as the user, not as root or another user; check the
generated unit file.

## "Frostytånegl-DefiasBrotherhood-EU" matches in `--player-name "frostytanegl"`

**Symptom.** Player-spec resolution still works when the user passes
`--player-name` without the diacritics or realm-region suffix.

**Cause.** Combat-log source names come as `Name-Realm-Region`. Naïve
case-insensitive comparison fails for both the diacritic and the suffix.

**Workaround.** `StringNormalization.FoldForCompare`
([`src/Wipelog.Core/Detection/StringNormalization.cs`](../src/Wipelog.Core/Detection/StringNormalization.cs))
NFD-decomposes the string and strips non-spacing marks, and the
resolver compares both the full name and the bare-name part before
the first dash. CLAUDE.md's "Player identity resolution" section
covers the three-path fallback the resolver uses.

If you ever rewrite identity matching, run
`tests/Wipelog.Core.Tests/PlayerSpecResolverTests.cs` first — the
Frostytånegl case is in there.

## Schema downgrade fails with "newer than this build supports"

**Symptom.** A new daemon writes the DB at `user_version = 9`, then you
launch an older binary — it refuses to open the DB.

**Cause.** `ClipDatabase.EnsureMigrated` throws if `user_version >
CurrentSchemaVersion` (see
[`ClipDatabase.cs:84`](../src/Wipelog.Library/ClipDatabase.cs)).

**Workaround.** Use `--db-path /tmp/test.db` to point an older binary
at a fresh DB while you debug, or upgrade the binary you're testing
against. There is no automatic downgrade path and there should not
be: a forward migration may have dropped a column the old code needs.
