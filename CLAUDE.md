# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```sh
dotnet build                         # build all four projects
dotnet test                          # run all 500+ tests
./publish.sh                         # self-contained single-file binaries → dist/{daemon,app}/
```

**Run a subset of tests.** This project uses `xunit.v3` with
`Microsoft.Testing.Platform`, so the usual `dotnet test --filter "FullyQualifiedName~..."`
syntax doesn't apply. Either invoke the test binary directly or use the new
flag names:

```sh
# After dotnet build, run the test exe with class/method filters:
cd tests/Wipelog.Core.Tests/bin/Debug/net10.0 && \
  ./Wipelog.Core.Tests \
    --filter-class "Wipelog.Core.Tests.PlayerSpecResolverTests" \
    --filter-method "*Mine*"  # wildcards allowed at start/end
```

**Deployment.** The user runs from `~/bin/wipelog/` (framework-dependent
layout, not the self-contained `dist/`). After daemon changes, redeploy with:

```sh
dotnet publish src/Wipelog.Daemon -c Release --no-self-contained \
  -o ~/bin/wipelog
dotnet publish src/Wipelog.App    -c Release --no-self-contained \
  -o ~/bin/wipelog-app
```

The launcher script `~/bin/wipelog/wow-record.sh` execs the daemon with
the user's WoW logs/output paths baked in.

**Daemon subcommands** (`Wipelog.Daemon <subcommand>`):
- *(none)* — live recording mode; tails the combat log and drives gpu-screen-recorder.
- `index --output <dir>` — reconcile the SQLite library with on-disk sidecars (also runs on daemon startup).
- `backfill-events --output <dir> --log-dir <dir> [--force]` — generate (or with `--force`, regenerate) `.events.json` sidecars from existing combat logs. Run with `--force` after upgrading past schema v2 to backfill the new `combatants` list into old clips (needed for the raid-comp chip strip in the details panel).
- `apply-overlay --clip <file> [--output <file>] [--in-place] [--x N --y N --width N --height N]` — apply the chat-overlay mask to an existing clip (for dialing in the region / checking hardware-encode timing without recording). Region defaults to `chatOverlay` in `preferences.json`; flags override. Writes a sibling `*.masked.<ext>` by default (original preserved) unless `--in-place`. Forces the mask on regardless of the saved `enabled` toggle.

## Architecture

Four projects with strict dependency direction:

```
Wipelog.Core  ←  Wipelog.Library  ←  Wipelog.Daemon
                  ←  Wipelog.App       ←  Wipelog.Library
```

- **Core**: pure logic. Combat-log parser, file tailer, encounter state machine, recorder process wrapper, recording event buffer. No DB, no UI. Library only depends on Core for `ContentType` + `PlayerIdentity`.
- **Library**: SQLite (Dapper) clip repository, schema migrations, indexer.
- **Daemon**: generic-host console app. Wires Core+Library, owns `LogTailer` → `EncounterDetector` → `RecorderController` → `LibrarySink`.
- **App**: Avalonia 11 GUI with embedded LibVLCSharp. Uses Library directly. CommunityToolkit.Mvvm for view models.

See `docs/architecture.md` for the phase-by-phase history.

### Recording lifecycle

`LogTailer` (FileSystemWatcher + byte-offset tracking) → `Channel<CombatLogEvent>` → `RecorderController.RunAsync`. The controller feeds events to `EncounterDetector` (pure state machine emitting `StartRecording`/`StopRecording`) plus two buffers:

- **Identity buffer** — collected for the spec resolver. `PlayerSpecResolver` runs once at finalize time.
- **`RecordingEventBuffer`** — collects `UnitDied` + `EncounterStart` for the timeline; **attributes kill blows the moment `UnitDied` is observed**, not at `Build()` time (a previous bug evicted the relevant damage events before Build ran — see commits `f80f6f4` / `4ca988c`).

On `StopRecording`, the recorder writes to a hidden temp file (`.NAME.recording.mp4`) and renames it on success. Three sidecar files end up next to the `.mp4`:

- `<stem>.json` — main clip metadata (v2 sidecar).
- `<stem>.events.json` — `TimelineEventDocument` (versioned, v1 today).
- DB row inserted via `IClipRecorderSink` → `LibrarySink`.

### Pre-encounter buffer (`--preroll-seconds`)

Opt-in. When the daemon is launched with `--preroll-seconds N` (2..86400), a second `gpu-screen-recorder` runs in replay-buffer mode alongside the main one, keeping the last N seconds of gameplay in RAM. The lifecycle:

- DI: `Wipelog.Daemon/Program.cs` registers `PreRollRecorder` (wrapping a separate `GpuScreenRecorder` with `ReplayBufferSeconds=N`) and passes it through to `RecorderController`.
- `RecorderController.RunAsync` starts the pre-roll process before draining the event channel; in `OnStartAsync` it calls `IPreRollRecorder.SaveAsync` (SIGUSR1 + poll the pre-roll dir for the new mp4); in `FinalizeAsync` it concatenates `<preroll>.mp4` + `<final>.mp4` via `FfmpegConcat` (stream-copy, both inputs share encoder settings) and replaces the final file. `FfmpegProbe` reads the actual prepended duration; both helpers fall back silently to no-op when ffmpeg/ffprobe aren't on PATH.
- Pre-roll files land in `<output>/.preroll/`. The dot-prefix keeps the dir out of `LibraryIndexer` (TopDirectoryOnly) and `LibraryWatcher` (`IncludeSubdirectories=false`); the source snippet is deleted on every concat attempt (success or fail) so the dir doesn't accumulate.
- Both sidecars grow an optional `preRollSeconds: double` field on successful concat — the main `.json` (read by the indexer) and the `.events.json` (read by the timeline VM). The DB schema is unchanged. v1 sidecar readers ignore the new field.
- On any failure (gsr crash, SaveAsync timeout, ffmpeg missing, concat exit ≠ 0) the controller logs and keeps the original (un-prepended) clip — pre-roll is best-effort.
- **Timeline event offset.** `TimelineEvent.OffsetMs` stays encounter-relative (T=0 = `EncounterStart`); the UI shifts it. `TimelineViewModel` exposes `PreRollMs` and redefines `ClipDurationMs` to mean **video** duration (encounter + pre-roll). `ScrubBar` and `TombstoneStrip` add `PreRollMs` to each event's `OffsetMs` before projecting to X, and emit seek targets in video-time. The `PlayerView.OnTombstoneMarkerClicked` death rewind also adds `PreRollMs` back in because it bypasses the strip's pre-computed seek to honour user `SeekBackSeconds`. Older clips (no pre-roll) have `PreRollMs = 0` and behave identically.

### Chat overlay masking (privacy)

Opt-in. When `chatOverlay.enabled` is set in `preferences.json`, the daemon composites a black panel branded with the Wipelog logo + wordmark over a configured screen region (`x`/`y`/`width`/`height` in pixels — `x` from the left, **`y` from the bottom** edge so the mask glues to where WoW chat sits; built once by `ChatOverlayProcessor.BuildDrawboxFilter` as `y=ih-Y-Height`, which also keeps it resolution-independent vertically) of every saved clip before it is indexed — hides chat/whispers/names from shared clips. Default off. Also available on-demand: GUI right-click → "Create masked copy for sharing" (a `Manual` clip in the Clips tab) and the `apply-overlay` subcommand.

- `ChatOverlayProcessor` (`Wipelog.Core/Recording/`, static, sibling to `FfmpegConcat`/`FfmpegProbe`) shells `ffmpeg`: a `drawbox` black box with the Wipelog-branded PNG (embedded resource `wipelog-mask.png`, extracted to temp) overlaid via `filter_complex` — scaled-to-fit + centred so it never distorts; falls back to a plain `drawbox` box if the asset can't be loaded. A pixel filter can't stream-copy, so the **video is re-encoded** (`-c:a copy` keeps audio). `MaskToFileAsync(source, dest)` masks to a new file (used by the GUI on-demand copy); `ApplyAsync` wraps it for in-place masking (recording flow + `apply-overlay`). `RecorderController.TryApplyChatOverlayAsync` runs it **right after** the pre-roll prepend in `FinalizeAsync` so the mask covers the full video, before sidecars/sink.
- **Encoder tiering** (`SelectEncodePlan`): NVENC (`*_nvenc`, requires a real `/dev/nvidia*` device node — `NvidiaDevicePresent`) → VAAPI (`*_vaapi`, requires a `/dev/dri/renderD*` node) → software (`libx264`/`libx265`/`libsvtav1`), matched to the probed input codec family. The NVIDIA-device gate matters because ffmpeg advertises `*_nvenc` whenever it was *compiled* with NVENC support — even on AMD/Intel boxes with no NVIDIA GPU; selecting it there fails at runtime (`CUDA_ERROR_NO_DEVICE`) and would skip past VAAPI straight into slow software. VAAPI builds a `drawbox,format=nv12,hwupload` filter chain + `-vaapi_device`. A hardware encode that exits non-zero **auto-retries once in software**. The whole step is best-effort: any failure keeps the original unmasked clip and fires `NotifyErrorAsync`.
- **Quality + speed tuning** (`QualityArgs`/`BuildArgs`): re-encode uses **constant quality** (NVENC `-cq 26`, VAAPI `-qp 26`, x264/x265 `-crf 23/26`, SVT-AV1 `-crf 30`) — ffmpeg's *default* NVENC rate control drops the bitrate ~10x, so this is required to avoid degrading the clip. NVENC also decodes on the GPU (`-hwaccel cuda`) and uses the fastest preset (`-preset p1`); with constant quality the preset only trades file size, not visible quality. Masking is inherently a full re-encode (~6x realtime for 1440p60 on an RTX 3070), so a multi-minute pull takes ~1 min.
- Read at daemon **startup** (not hot-reloaded); not part of the systemd unit, so GUI edits don't trip `HasPendingUnitChanges` — the section's note tells users to reinstall. `apply-overlay` subcommand exists for testing on existing clips.
- **GUI "Test mask"** (`SettingsViewModel.TestMaskAsync`): x11grabs a single frame and paints the same `BuildDrawboxFilter` box on it. It crops to the monitor named in `captureTarget` (or the largest, for `focused`/`portal`/`region`) using `XrandrMonitorList` (`Wipelog.Core/Recording/`) — `gpu-screen-recorder --list-monitors` only gives name+size, but `xrandr --listmonitors` also gives the X11 pixel offset x11grab needs to crop one head out of a multi-head root. The connector names match between the two on X11. Falls back to the whole root (and a status note) when xrandr is missing or on Wayland.

### Daemon control socket (GUI Record/Stop buttons)

The App talks to the running daemon over a Unix-domain socket at `$XDG_RUNTIME_DIR/wipelog/control.sock` (`/tmp/wipelog-{uid}/control.sock` fallback when `XDG_RUNTIME_DIR` isn't set). Both sides compute the path via `Wipelog.Core.Control.ControlSocketPath.Resolve`. The protocol is one JSON request → one JSON response → close.

Commands recognised today:
- `stopRecording` — finalises the active clip. Success is type-aware: `ContentType.Manual` → `success=true`; everything else (raid pull, M+ run, PvP) → `success=false`. No-op if idle (answers `ok=false`).
- `startManualRecording` — starts a free-form Manual recording. Refused if a recording is already in progress.

Unknown commands answer `ok=false` so adding more (status query, force restart, etc.) is additive — no version bump needed.

- **Server**: `src/Wipelog.Daemon/Control/ControlServer.cs` — a `BackgroundService` that listens on the socket and dispatches commands to `RecorderController`. Stale socket files from a crashed prior run are unlinked at startup (single-instance daemon model). Request payload is capped at 4 KiB; read timeout is 5 s.
- **Client**: `src/Wipelog.App/Services/DaemonControlClient.cs` — opens a fresh connection per call, returns `ControlCallResult { Status, State, Error }` with three statuses: `Success`, `Failure` (daemon answered `ok=false`), `Unreachable` (socket missing / connect failed / no response).
- **Stop semantics**: `RecorderController.StopCurrentAsync` takes the same internal `_stateLock` as the event-loop body so a Stop click can't race with a `StartRecording` / `StopRecording` arriving from the combat log. Internally it calls `SafeStopAsync(success)` with `success = activeType == Manual`. The Stop button is wired into the sidebar Status block (only visible when `IsRecording`) via `DaemonStatusViewModel.StopRecordingCommand`.
- **Manual recording**: `RecorderController.StartManualAsync(startedAt, ct)` synthesises a `StartRecording` with `ContentType.Manual`, `DisplayName = "Manual recording - YYYY-MM-DD HH:MM"`, and filename stem `<timestamp>_manual`. Same lifecycle as encounter-triggered clips — pre-roll prepend, identity buffer, lifecycle observer. The Record button is shown in the sidebar Status block when `IsIdle` via `DaemonStatusViewModel.StartManualRecordingCommand`. Manual clips land in the "Clips" rail tab (`ClipFiltersViewModel.BuildQuery` maps `ContentTypeFilter.Clip` → `[ContentType.Manual]`).

### First-run onboarding

The Settings view shows an amber "Welcome - finish setting up Wipelog" banner above the tab body whenever `SettingsViewModel.IsOnboardingNeeded` returns true. The signal is simply `string.IsNullOrWhiteSpace(LogDirectory)` — output dir and daemon exec path have safe defaults, so the combat-log path is the only field that gates the banner.

- **Detector**: `src/Wipelog.App/Services/WowLogDetector.cs` (`IWowLogDetector`) scans well-known Linux WoW launcher prefixes — Lutris (`~/Games`, `~/.local/share/lutris/runners/winewow`), Heroic (`~/Games/Heroic/Prefixes/default`), Bottles (flatpak + native), raw Wine (`~/.wine`) — and looks for the canonical suffix `drive_c/Program Files (x86 or 64-bit)/World of Warcraft/_retail_/Logs/WoWCombatLog-*.txt`. Returns `DetectedLogPath { Path, LogFileCount, NewestLogAt }` sorted newest-first so the live install bubbles up when stale prefixes also exist. Constructor takes the search-root list so tests inject synthetic temp directories.
- **VM**: `SettingsViewModel.DetectWowLogsCommand` runs the detector on a thread-pool task and marshals results back to the UI thread; `ApplyDetectedPathCommand(DetectedLogPath)` sets `LogDirectory`, clears the candidates collection, and jumps to the Service tab so the install button is one click away. The detection message ("Found N candidates", "No WoW combat-log directories detected") surfaces below the Detect button.
- **View**: `src/Wipelog.App/Views/SettingsView.axaml` adds a third row to the page grid (`48,Auto,*`) for the banner. Inside: a header label, a Detect button, a status line, and an `ItemsControl` of per-candidate chip buttons. Each chip's `Command` is bound through the parent `ItemsControl`'s DataContext (`$parent[ItemsControl].((vm:SettingsViewModel)DataContext).ApplyDetectedPathCommand`) with the `DetectedLogPath` as parameter.

### Advanced Combat Logging (ACL) warning

Wipelog relies on the 17-field advanced log block for kill-blow attribution, boss HP%, and the raid-comp chip strip. When the user has WoW's Advanced Combat Logging disabled, the combat log header reports `ADVANCED_LOG_ENABLED=0` and those features silently produce empty data. We surface the problem instead of hiding it:

- **Detector**: `RecorderController` forwards every parsed `CombatLogVersion` event to an optional `IAdvancedLoggingObserver` (interface in `src/Wipelog.Core/CombatLog/IAdvancedLoggingObserver.cs`).
- **Monitor**: `src/Wipelog.Daemon/Logging/AdvancedLoggingMonitor.cs` gates on state transitions — first ACL=0 fires a `notify-send` toast and flips `HeartbeatState.SetAdvancedLoggingDisabled(true)`; subsequent ACL=0 headers (WoW re-emits the line on every `/reload`) are no-ops. ACL=1 silently clears the flag and re-arms the notifier so a later disable will fire again.
- **Status**: `DaemonStatus.AdvancedLoggingDisabled` (default `false`, opt-in init field — pre-Tier1-#8 clients deserialise without false positives) flows through the heartbeat JSON to the GUI. `FileBasedDaemonStatusReader.StatusEquals` includes the flag so the sidebar VM gets notified of transitions.
- **GUI**: `DaemonStatusViewModel.AdvancedLoggingDisabled` projection feeds an amber warning row in the sidebar Status block (visible whenever the flag is set, regardless of recording state). The tooltip carries the actionable fix path: WoW → Network options → Advanced Combat Logging.

### Recorder-failure warning (sticky "clips may be lost" row)

A misconfigured capture (e.g. stale monitor name after an X11→Wayland switch: gsr exits 51 with "display not found") used to lose clips silently — the only trace was the journal. Capture failures now surface as a persistent red row in the GUI sidebar.

- **Signal**: `IRecordingLifecycleObserver` grew two default-implemented methods — `OnRecorderUnhealthyAsync(message)` and `OnRecorderHealthyAsync()`. `RecorderController.NotifyCaptureFailureAsync` fires *both* the existing `OnRecordingErrorAsync` (toast + transient `LastError` tooltip) and the sticky unhealthy mark. Call sites: recorder died mid-recording, recorder failed to start, temp file missing at finalize, pre-roll `StartAsync` threw, and — new — the pre-roll process's `ExitedUnexpectedly` event (the controller now subscribes; reported fire-and-forget via `Task.Run` because the daemon may sit idle with no combat-log traffic to tick the event loop, and this failure typically happens at daemon startup, hours before the first pull).
- **Clearing**: `OnRecorderHealthyAsync` fires at the end of `FinalizeAsync` when a clip finalized with a real video file — *except* when that finalize was itself triggered by the recorder dying (`_finalizingAfterRecorderDeath` flag; a salvaged partial file is not proof of health). No time-based retention (unlike `LastError`'s 5-minute window) — a broken recorder stays flagged across an idle night until capture provably works or the daemon restarts.
- **Plumbing**: `HeartbeatState.SetRecorderError(string?)` → `DaemonStatus.RecorderError` (JSON `recorderError`, null default so old payloads deserialise) → `FileBasedDaemonStatusReader.StatusEquals` includes it → `DaemonStatusViewModel.RecorderError`/`HasRecorderError` → red `DangerBadgeBackgroundBrush` row in `MainWindow.axaml` sidebar footer ("Recorder error — clips may be lost"), full message incl. gsr's stderr tail in the tooltip.
- Known trade-off: pre-roll-only breakage is cleared by any successful main-recorder finalize (both processes share target/settings, so in practice they break together).

### Content recording filters (CONTENT settings section)

Opt-in filters let users skip content they don't care about (record Mythic raids only, ignore +2 keys, skip BGs). Defaults record everything — users opt into being more restrictive.

- **Model**: `RecordingFilters` (`src/Wipelog.Core/Detection/RecordingFilters.cs`) holds `RecordRaids`/`RecordMythicPlus`/`RecordBattlegrounds` bools, a `RaidDifficulties` set (as `RaidDifficulty` enum: LFR/Normal/Heroic/Mythic), and `MinKeystoneLevel`. Pure `AllowsRaid`/`AllowsMythicPlus`/`AllowsBattleground(out reason)` methods carry a human-readable skip reason. **Empty `RaidDifficulties` = no filter** (record all). **Unknown difficulty IDs are never filtered.** Arena/Solo Shuffle have no toggle (always recorded). Stored as `IReadOnlyList` not `IReadOnlySet` because System.Text.Json can't deserialize `IReadOnlySet`; difficulty IDs map via `DifficultyMap.ToRaidDifficulty` (14/15/16/17 → tier).
- **Persistence**: top-level `recordingFilters` block on `UserPreferences` (camelCase keys, difficulties as a string array). `Load()` restores defaults when the block is missing/null (mirrors the `Daemon ??=` guard).
- **Daemon**: reads filters from `preferences.json` at startup (`Program.cs`, same `userPrefs` load used for notifications) and injects them + an `ILogger<EncounterDetector>` into `EncounterDetector`. **Filters hot-reload** — `RecordingFilterReloader` (`src/Wipelog.Daemon/Configuration/`, a hosted `BackgroundService`) `FileSystemWatcher`es `preferences.json` (debounced 300 ms; watches Changed/Created/Renamed to catch the GUI's tmp+rename atomic save) and pushes the new filters into the live detector via `EncounterDetector.UpdateFilters`. That swap is thread-safe because `_filters` is `volatile` and the detector reads it only at the `Idle`→start transition, so an in-flight recording is never disturbed. Only the *filters* hot-reload; the daemon's other settings are baked into its CLI args and still need a reinstall. The detector gates each `Idle`-state start on the matching `Allows…`; on reject it logs at **Information** (`"Skipping recording: raid Heroic <name> filtered (RaidDifficulties does not include Heroic)"`) and stays Idle. Decision stays pure in `RecordingFilters`; the detector only logs.
- **GUI**: CONTENT section at the **top** of the Recording tab (`SettingsView.axaml`), above VIDEO. Three toggles; dependent controls (difficulty chips, keystone `NumericUpDown` 2..30) bind `IsVisible` to their toggle. Difficulties use the generic `ChipSelector` control (`src/Wipelog.App/Controls/ChipSelector.axaml`) — a reusable multi-select that moves items between `SelectedItems`/`AvailableItems`; move logic is in the pure `ChipSelectorLogic`. `SettingsViewModel` holds the bindable state and maps to/from `RecordingFilters` via the pure `RaidDifficultyChips` helper (so it's testable without the LibVLC/Dispatcher-bound VM). Persisted through the existing `PersistUserPrefs` path. Filter edits take effect on the running daemon within ~1 s via the hot-reload watcher (no restart). **Pending-unit-changes hint**: settings that *do* require a restart (video/audio/framerate/codec/pre-roll/paths — anything baked into the systemd unit's `ExecStart`) surface an amber "Settings changed — reinstall now" banner in the Service tab. `SettingsViewModel.HasPendingUnitChanges` is computed by re-running `SystemdUnitFileBuilder.Build(CurrentDaemonConfig())` and comparing the normalized text to the on-disk unit file (the installer writes exactly that builder output, so the diff is exact and filters — which never appear in the unit — never trip it). Recomputed in `PersistUserPrefs` and on `ServiceState` changes.

### Player identity resolution

`PlayerSpecResolver.Resolve` tries three paths in order:

1. **AFFILIATION_MINE flag** — `sourceFlags & 0xF == 0x1` is canonical: only the recording player has it. Looking at `UnknownEvent.Fields[2]` (hex string like `"0x511"`).
2. **Explicit `--player-name` hint** — diacritic- and case-insensitive name folding (`StringNormalization.FoldForCompare` decomposes NFD + strips non-spacing marks). Matches against both the full source name and the bare-name part before the first dash.
3. **Frequency fallback** — count `Player-*` source GUIDs, pick the most common. Last resort; in raids the tank often out-events the actual recorder.

Source names in combat logs come as `Name-Realm-Region` (e.g. `"Wonsupi-DefiasBrotherhood-EU"`). `PlayerIdentity.ParseFullName` splits these into separate fields; the DB has `player_name`, `player_realm`, `player_region` as distinct columns.

### Damage event parsing

Damage suffix is always **the last 10 fields** of a damage line (`amount, overkill, school, resisted, blocked, absorbed, critical, glancing, crushing, isOffHand`). Count from the end — that survives the variable-width advanced-logging block (17 extra fields when `ADVANCED_LOG_ENABLED=1`). `BuildDamageEvent` handles both spell-prefix damage (`SPELL_DAMAGE`, `SPELL_PERIODIC_DAMAGE`, `RANGE_DAMAGE`, `SPELL_BUILDING_DAMAGE`) and no-prefix swing damage (`SWING_DAMAGE`, `SWING_DAMAGE_LANDED`).

### SQLite schema and migrations

DB lives at `~/.local/share/wipelog/clips.db` by default (override with `--db-path`). Migrations are embedded SQL resources at `src/Wipelog.Library/Migrations/NNN_*.sql`, applied in order based on `PRAGMA user_version`. To add one: create the next-numbered file, set the last statement to `PRAGMA user_version = N;`, and bump `CurrentSchemaVersion` in `ClipDatabase.cs`.

Indexer flips DB state to match filesystem reality. The `has_events` column tracks `.events.json` presence; hand-deleting the file flips it back to false on next reconcile. Always upsert with `COALESCE(excluded.field, clips.field)` for identity columns so re-indexing doesn't clobber backfilled data.

### Sidecar JSON

Both `.json` and `.events.json` use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so non-ASCII names (e.g. `"Frostytånegl"`) stay readable instead of escaped to `å`. The `.events.json` is versioned (currently `version: 2`) and uses `[JsonPolymorphic]` discriminator (`"kind"`) — readers should ignore unknown kinds rather than fail. v2 added a top-level `combatants` list (one record per player GUID with `specId`, `class`, `spec`, `role`) for the raid-comp chip strip; v1 readers see the absent field as an empty list and stay functional.

### Bulk operations

Multi-select in `Views/ClipsTableView.axaml` uses Avalonia's native DataGrid Extended selection (Ctrl/Shift + click); the code-behind mirrors `DataGrid.SelectionChanged` into `ClipListViewModel.SelectedItems`. The status footer flips between the count line and a `[Delete] [Archive] [Clear]` action bar based on `HasSelection`. Bulk delete goes through `IClipRepository.DeleteClipsAsync` — single DB transaction across all rows, best-effort file sweep of the three files per clip (video + sidecar + events sidecar). Bulk archive streams selected clips into a single `.zip` via `Services/ClipArchiver.cs` (mp4 stored uncompressed, sidecars deflated fastest); the modal `ProgressDialog` posts per-clip progress and routes Cancel/Esc/window-close through a single `CancellationTokenSource`. `SelectedItems` is cleared in `RefreshAsync` because a filter change replaces item instances wholesale.

### PvP content types

The `ContentType` enum (`Core/Detection/RecordingCommand.cs`) recognises five non-PvE values alongside `RaidEncounter` and `MythicPlus`: `Arena`, `SoloShuffle`, `Battleground`, `RatedBattleground`. Arena and Solo Shuffle are detected from `ARENA_MATCH_START` (Solo Shuffle is disambiguated by the bracket field containing `"Shuffle"`); BGs are detected from `ZONE_CHANGE` into a known BG map ID. The whitelist of BG zone IDs lives at `Core/Detection/EncounterDetector.cs` (`BattlegroundZoneIds`) — extend it when you have a real combat log showing a new BG zoneId. RBG vs unrated BG isn't yet distinguished (zoneIds appear shared); the rail's Battlegrounds tab folds in both via the filter VM. `ArenaBracket` ("Arena3v3", "Rated Solo Shuffle", etc.) persists on the clip row through migration `005_arena_bracket.sql` and on the sidecar JSON. **Arena win/loss** is resolved at finalize in `RecorderController.ResolveArenaOutcome`: when both `ArenaMatchEnd.WinningFaction` and `PlayerIdentity.Faction` (read from `COMBATANT_INFO` field [2]) are known, the placeholder `_complete.mp4` suffix is rewritten to `_win.mp4` or `_loss.mp4` and the row's `Success` reflects the outcome; Solo Shuffle and BG keep the neutral `_complete` placeholder until a clean signal lands.

### Filter bar

The date filter is a button-with-flyout (`Views/DateRangeFlyoutView.axaml`) backed by `ClipFiltersViewModel.RangeMode` (preset radios: Last 7/30/90 days, All time) plus nullable `CustomStart` / `CustomEnd` for free-pick ranges. Setting either custom date flips mode to `Custom` automatically; `BuildQuery` projects to `ClipQuery.Since` and `ClipQuery.Before` (inclusive-end-of-day: a user's "Mar 15" becomes `Before = Mar 16` midnight so the SQL `< @Before` clause includes everything that day). For end-to-end testing the range picker without WoW, `scripts/make-test-clip.sh --started-at <iso>` backdates a generated clip.

### Clip details panel

The right-hand 300px column in `MainWindow.axaml` hosts `Views/ClipDetailsView.axaml`. The view's single metadata grid has 12 IsVisible-gated rows: three always-visible (Started, Duration, Size), then per-type rows driven by `ClipDetailsViewModel.IsRaid` / `IsMythicPlus` / `IsArena` / `IsSoloShuffle` / `IsBattleground` — Encounter+Difficulty+Instance for raids, Dungeon+Keystone for M+, Bracket for Arena/Solo Shuffle, Map for Battlegrounds — plus Character/Spec gated by `HasCharacter` / `HasSpec`. The comp-chip strip (🛡 / ✚ / ⚔ / 💀) sits above the grid and is fed by `IClipEventsLoader` reading the `.events.json` sidecar's `Combatants` list; `HasAnyBadge` hides the whole strip when no data is available. Favorite + delete deliberately aren't surfaced as buttons here — both already live on the table's context menu (`ClipsTableView.axaml.cs` → `ClipListViewModel.DeleteSingleCommand`). Deferred: M+ affixes (needs `ChallengeModeStart.Affixes` plumbed into the sidecar), Solo Shuffle / BG win/loss (no clean single signal), MMR delta (no data source).

### Clip notes

Per-clip free-form notes persist on the `notes` column added in migration `006_clip_notes.sql` (schema v6). `IClipRepository.SetNotesAsync` normalises whitespace-only input to null so "user cleared the textarea" and "never typed" share a single on-disk state. `ClipDetailsViewModel.NotesText` loads from the selected clip's column on selection change; `CommitNotesAsync` writes back on `TextBox.LostFocus` (wired in `ClipDetailsView.axaml.cs`) and refreshes the cached `ClipListItemViewModel` record so a re-select doesn't overwrite the textbox with the pre-edit value. The view also runs a compiled regex `\b(\d{1,3}):(\d{2})\b` over `NotesText` (`ClipDetailsViewModel.ExtractTimestamps`) and renders one chip per detected `MM:SS` token below the editor — clicking a chip seeks the player via `PlayerViewModel.SeekAbsolute`. Seconds ≥ 60 drop silently; duplicate labels collapse to one chip.

### Test conventions

- xunit.v3 + Microsoft.Testing.Platform. Project sets `<UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>`. Tests use `TestContext.Current.CancellationToken` (not `xunit.NET.Test.CancellationToken`).
- Fixture files live under `tests/Wipelog.Core.Tests/Fixtures/` and must be added to the csproj's `<None Update="...">` list with `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`.
- `FakeClock` (not `FixedClock`) is the test clock. `TempDirectory` is the standard scratch-dir helper.
- The Daemon project has `<InternalsVisibleTo>Wipelog.Core.Tests</InternalsVisibleTo>` so tests can reach `internal` types like `EventsBackfiller`.

### Common pitfalls

- The combat-log line splitter handles top-level commas only; `[1,2,3]` arrays and `"quoted, names"` stay as one field. Don't write a new CSV parser.
- The recording controller resolves player identity **once at finalize time**, not continuously. Don't move the resolver call back into the per-event loop — it logged warnings every 25 events in an earlier iteration.
- `RecordingEventBuffer.EvictExpired` runs on every `AddDamage` call and walks the cutoff forward across **all buckets**. This is fine because kill-blow attribution happens at `Observe(UnitDied)` time, not at `Build()`. If you ever move attribution back to Build, eviction will silently wipe the data you need.
- For non-ASCII player names: write paths must use `UnsafeRelaxedJsonEscaping`. Read paths don't care.
- The user runs from `~/bin/wipelog/`, not from the source tree. After daemon changes, redeploy via the `dotnet publish --no-self-contained` line above before they can test.
