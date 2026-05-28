# Data formats

Five on-disk artefacts make up Wipelog's persistent state. This document
covers each one: where it lives, what shape it has, when it was added,
and which code is responsible for reading and writing it.

| Artefact | Path | Producer | Consumer |
|---|---|---|---|
| Clip sidecar | `<output>/<stem>.json` | `Wipelog.Daemon` (via `RecorderController.FinalizeAsync`) | `Wipelog.Library.LibraryIndexer` and the GUI |
| Timeline events sidecar | `<output>/<stem>.events.json` | `Wipelog.Daemon` (`RecorderController` + `EventsBackfiller`) | `Wipelog.App.Services.ClipEventsLoader` |
| Clip library | `~/.local/share/wipelog/clips.db` | Daemon writes, GUI reads (both via `Wipelog.Library`) | shared SQLite, WAL mode |
| Preferences | `~/.config/wipelog/preferences.json` | GUI writes (`UserPreferencesService.Save`), daemon reads (`UserPreferencesService.Load`) | both processes |
| Daemon heartbeat | `~/.cache/wipelog/status.json` | Daemon (`HeartbeatWriter`, every 2 s) | GUI (`FileBasedDaemonStatusReader`) |

All JSON is written with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` so
non-ASCII characters (e.g. `"Frostytånegl"`) survive round-tripping
without `\uXXXX` escapes. Property naming is camelCase across every
sidecar.

## Clip sidecar (`<stem>.json`)

Defined by `ClipSidecar` in
[`src/Wipelog.Core/Recording/ClipSidecar.cs`](../src/Wipelog.Core/Recording/ClipSidecar.cs).
The current version is `2`.

```json
{
  "version": 2,
  "contentType": "RaidEncounter",
  "displayName": "Vexie and the Geargrinders (Heroic)",
  "difficulty": "Heroic",
  "encounterId": 3009,
  "success": false,
  "startedAt": "2026-04-12T20:14:03.117+02:00",
  "endedAt": "2026-04-12T20:18:46.221+02:00",
  "durationSeconds": 283,
  "videoFile": "20260412_201403_vexie-and-the-geargrinders_heroic_wipe.mp4",
  "playerName": "Frostytånegl",
  "playerRealm": "DefiasBrotherhood",
  "playerRegion": "EU",
  "playerClass": "Death Knight",
  "playerSpec": "Frost",
  "instanceName": "Liberation of Undermine",
  "preRollSeconds": 30.04,
  "bossHpPercent": 21.4
}
```

- `version` — bumped when the writer adds a field that older readers
  must know about. v1 sidecars are still on disk in older installs and
  deserialise cleanly because every later field is nullable / optional.
- `contentType` — string form of `Wipelog.Core.Detection.ContentType`
  (`RaidEncounter`, `MythicPlus`, `Arena`, `SoloShuffle`, `Battleground`,
  `RatedBattleground`, `Manual`).
- `success` — for raids, true on a kill / false on a wipe; for Arenas,
  resolved at finalize by comparing winning faction with the player's
  faction (see CLAUDE.md "PvP content types"); for Manual recordings,
  always true; for Solo Shuffle / BGs, currently always false (no clean
  signal yet — see `docs/audit-core.md`).
- `preRollSeconds` — present only when `--preroll-seconds N` was set
  *and* the concat succeeded. Tells the player that the encounter T=0
  sits N seconds into the video, not at the start.
- `bossHpPercent` — raid wipes only; null on kills and on non-single-target
  encounters whose `ENCOUNTER_START` name doesn't identify a unit.
- `keystoneLevel` and `keyUpgradeLevel` — M+ only.
- `arenaBracket` — Arena / Solo Shuffle only (e.g. `"Arena3v3"`,
  `"Rated Solo Shuffle"`).

`null`-valued fields are omitted on write
(`DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`). Readers
must treat absent properties as "not known" rather than "false / zero".

## Timeline events sidecar (`<stem>.events.json`)

Defined by `TimelineEventDocument` in
[`src/Wipelog.Core/Recording/TimelineEvents.cs`](../src/Wipelog.Core/Recording/TimelineEvents.cs).
Current version is `2`.

```json
{
  "version": 2,
  "recordingStartedAt": "2026-04-12T20:14:03.117+02:00",
  "recordingEndedAt": "2026-04-12T20:18:46.221+02:00",
  "preRollSeconds": 30.04,
  "combatants": [
    { "playerGuid": "Player-3674-0A1B2C3D",
      "specId": 252, "class": "Death Knight", "spec": "Unholy", "role": "Damage" },
    { "playerGuid": "Player-3674-0E4F5A6B",
      "specId": 65, "class": "Paladin", "spec": "Holy", "role": "Healer" }
  ],
  "events": [
    { "kind": "BossStart",
      "offsetMs": 412, "encounterId": 3009, "encounterName": "Vexie and the Geargrinders" },
    { "kind": "PlayerDeath",
      "offsetMs": 152300, "playerGuid": "Player-3674-0E4F5A6B",
      "playerName": "Wonsupi-DefiasBrotherhood-EU",
      "killedBySource": "Geargrinder Bomb Specialist",
      "killedBySpell": "Pyroblast", "killingBlowAmount": 1843000 }
  ]
}
```

### Version history

| Version | Notes |
|---|---|
| 1 | `Events` only. `PlayerDeathEvent` and `BossStartEvent`. |
| 2 | Adds `Combatants` (for the raid-comp chip strip) and the optional `PreRollSeconds` field. V1 readers see the new fields as defaults (`Combatants = []`, `PreRollSeconds = null`) and remain functional. |

### Event polymorphism

Events are written with a `kind` discriminator. The reader uses
`[JsonPolymorphic]` with `IgnoreUnrecognizedTypeDiscriminators = true`,
so a future event kind added to the writer does **not** break older
readers — unknown kinds are silently skipped.

Today the recognised kinds are:

| `kind` | Fields | Emitted by |
|---|---|---|
| `BossStart` | `offsetMs`, `encounterId`, `encounterName` | `ENCOUNTER_START` inside M+ runs and raid encounters |
| `BossEnd` | `offsetMs`, `encounterId`, `encounterName`, `success` | `ENCOUNTER_END` (reserved; current readers display only the success bit) |
| `PlayerDeath` | `offsetMs`, `playerGuid`, `playerName`, `killedBySource?`, `killedBySpell?`, `killingBlowAmount?` | `UNIT_DIED`, attributed at observation time against the 5 s damage window in `RecordingEventBuffer` |

### Offset semantics

Every `offsetMs` is **encounter-relative**: T=0 = `recordingStartedAt`,
which equals the moment `EncounterDetector` emitted `StartRecording`.
When `preRollSeconds` is non-null, the underlying `.mp4` is longer than
the encounter — UI consumers must add `preRollSeconds * 1000` to each
event offset before computing scrub-bar X positions and player seek
targets. CLAUDE.md's "Pre-encounter buffer" section covers the
controller-side handling.

### Backfilling

The daemon subcommand `Wipelog.Daemon backfill-events --output <dir>
--log-dir <dir>` regenerates the sidecar from the original combat log
for clips that lack one. Use `--force` to overwrite existing sidecars —
needed once after upgrading past schema v2 to populate the new
`combatants` list into old clips.

## SQLite library

The database lives at `~/.local/share/wipelog/clips.db` by default
(override with `--db-path`). It is opened in WAL mode with
`synchronous = NORMAL` and `foreign_keys = ON` by every connection
([`ClipDatabase.cs:65`](../src/Wipelog.Library/ClipDatabase.cs)). Daemon
and GUI share it concurrently because WAL allows readers and one writer
in parallel without blocking.

### Migrations

Migrations are embedded SQL resources in
[`src/Wipelog.Library/Migrations/`](../src/Wipelog.Library/Migrations/),
named `NNN_*.sql`. Each ends with `PRAGMA user_version = N;`. On every
`OpenConnection()`, `ClipDatabase.EnsureMigrated` reads `user_version`
and runs every later migration in its own transaction up to
`CurrentSchemaVersion` (currently `8`).

| # | File | Adds |
|---|---|---|
| 001 | `001_initial.sql` | `clips`, `tags`, `clip_tags` tables. Indexes on `started_at DESC`, `content_type`, partial on `favorite WHERE favorite = 1`. |
| 002 | `002_player_and_instance.sql` | `player_name`, `player_realm`, `player_class`, `player_spec`, `instance_name`, `dungeon_name` columns + matching indexes. |
| 003 | `003_player_region.sql` | `player_region` column + index. |
| 004 | `004_events_sidecar.sql` | `events_sidecar_path` and `has_events` columns. `has_events` is the indexer's truth — hand-deleting the file flips it back to 0 on next reconcile. |
| 005 | `005_arena_bracket.sql` | `arena_bracket` column (e.g. `"Arena3v3"`, `"Rated Solo Shuffle"`). |
| 006 | `006_clip_notes.sql` | `notes` column for the per-clip free-form notes field. |
| 007 | `007_boss_hp_percent.sql` | `boss_hp_percent` column (raid wipes only). |
| 008 | `008_key_upgrade_level.sql` | `key_upgrade_level` column (M+ chest count: 0 = depleted, 1-3 = +1/+2/+3, NULL = unknown). |

To add a migration, drop in `0NN_short_name.sql` with the schema change
followed by `PRAGMA user_version = N;` and bump `CurrentSchemaVersion`
in [`ClipDatabase.cs`](../src/Wipelog.Library/ClipDatabase.cs).

### Upserts and the indexer

`LibraryIndexer.ReconcileAsync` is the bridge between sidecar JSON and
DB row. The upsert uses `COALESCE(excluded.field, clips.field)` for
identity-resolution columns (`player_name`, `player_realm`,
`player_spec`, etc.) so a re-index does not clobber data that's been
backfilled or hand-edited.

## Preferences (`~/.config/wipelog/preferences.json`)

Defined by `UserPreferences` and `DaemonConfig` in
[`src/Wipelog.Core/Preferences/UserPreferences.cs`](../src/Wipelog.Core/Preferences/UserPreferences.cs).
This file is owned by the GUI Settings view; the daemon reads it on
startup to pick up the notification toggles.

```json
{
  "closeToTray": true,
  "notifyOnClipSaved": true,
  "notifyOnErrors": true,
  "testRecordingSeconds": 5,
  "daemon": {
    "daemonExecutablePath": "$HOME/bin/wipelog/Wipelog.Daemon",
    "logDirectory": "$HOME/.wine/drive_c/Program Files (x86)/World of Warcraft/_retail_/Logs",
    "logPattern": "WoWCombatLog-*.txt",
    "outputDirectory": "$HOME/Videos/wow",
    "databasePath": "$HOME/.local/share/wipelog/clips.db",
    "gpuScreenRecorderExecutable": "gpu-screen-recorder",
    "captureTarget": "focused",
    "framerate": 60,
    "codec": "h264",
    "container": "mp4",
    "audioTracks": ["default_output"],
    "videoQuality": "medium",
    "prerollSeconds": 30
  }
}
```

The legacy single-source `audioSource` field is migrated into
`audioTracks` on load and then dropped; old preferences files still
deserialise. The `daemon` sub-object's values are written into the
systemd unit's `ExecStart` line when the user clicks "Install daemon"
in Settings — they are not read by the daemon at runtime (the CLI
flags from the unit file are authoritative).

Save is atomic via `*.tmp` rename. Load is best-effort: any parse
failure falls back to `new UserPreferences()` with home-relative
defaults.

## Daemon heartbeat (`~/.cache/wipelog/status.json`)

Defined by `DaemonStatus` in
[`src/Wipelog.Core/Status/DaemonStatus.cs`](../src/Wipelog.Core/Status/DaemonStatus.cs).
Path resolved by `HeartbeatWriter.DefaultStatusPath()` against
`$XDG_CACHE_HOME` (default `~/.cache`). Rewritten every 2 s by
[`HeartbeatWriter`](../src/Wipelog.Daemon/Status/HeartbeatWriter.cs) via
atomic temp-file rename, so the GUI's poller never reads a half-written
file. Deleted on clean shutdown.

```json
{
  "version": "0.7.0",
  "pid": 91234,
  "startedAt": "2026-05-19T08:01:12.041+02:00",
  "heartbeatAt": "2026-05-19T08:14:55.730+02:00",
  "state": "Recording",
  "currentRecording": {
    "contentType": "MythicPlus",
    "displayName": "The Stonevault +12",
    "startedAt": "2026-05-19T08:14:32.001+02:00",
    "keystoneLevel": 12
  },
  "clipsRecordedThisSession": 4,
  "lastError": null,
  "advancedLoggingDisabled": false
}
```

- `state` is the `DaemonState` enum: `Idle` or `Recording`.
- `currentRecording` is null when `state == Idle`.
- `advancedLoggingDisabled` is opt-in (default `false`), so a heartbeat
  written by an older daemon binary deserialises without a false
  positive in the GUI.
- `lastError` carries a short, user-presentable string for the
  sidebar's error pill; cleared once the daemon recovers.

The GUI's poller (`FileBasedDaemonStatusReader`) re-reads the file on
each tick. If the file is missing, older than 30 s, or unparseable, the
sidebar shows "Daemon not running".

## Combat log

Wipelog reads but does not write the WoW combat log. Field-by-field
documentation lives in upstream resources (the
[`wow-combat-log-events`](https://wowpedia.fandom.com/wiki/COMBAT_LOG_EVENT)
reference on Wowpedia is the canonical one). The two implementation
notes worth knowing here:

- The line splitter in `Wipelog.Core/CombatLog/CombatLogFieldSplitter.cs`
  handles top-level commas only — `[1,2,3]` arrays and `"quoted, names"`
  stay as a single field. Don't replace it with a generic CSV parser.
- The damage suffix is always the **last 10 fields** of a damage event
  (`amount, overkill, school, resisted, blocked, absorbed, critical,
  glancing, crushing, isOffHand`). Counting from the end survives the
  variable-width 17-field advanced-logging prefix. See CLAUDE.md's
  "Damage event parsing" section for details.
