# Wipelog

Linux-native World of Warcraft gameplay recorder. A daemon watches WoW's
combat log to detect raid encounters, Mythic+ runs, and PvP, and drives
[`gpu-screen-recorder`](https://git.dec05eba.com/gpu-screen-recorder/) to
capture only those segments — hardware-encoded on NVIDIA (NVENC), AMD/Intel
(VAAPI), or software. A separate Avalonia GUI browses, filters, plays, tags,
and curates the resulting clips, all backed by a shared SQLite library.

## Documentation

- [`docs/`](docs/) — system overview, on-disk formats, daemon and GUI
  internals, contributor guide, troubleshooting, and per-area audit
  reports. Start at [`docs/README.md`](docs/README.md).
- [`docs/architecture.md`](docs/architecture.md) — two-process model,
  IPC channels, data flow, and the phase-by-phase project history.
- [`docs/contributing.md`](docs/contributing.md) — build, test,
  publish, commit conventions, and where each kind of change belongs.
- [`CLAUDE.md`](CLAUDE.md) — implementation notes (recording lifecycle,
  identity resolution, damage parsing, common pitfalls) maintained as
  a living spec.

## Features

**Recording (daemon).** Tails the WoW combat log and drives
gpu-screen-recorder to capture only the segments worth keeping:

- Detects raid encounters, Mythic+ runs, and PvP (Arena, Solo Shuffle,
  Battlegrounds), each tagged with its metadata — difficulty, keystone level,
  arena bracket, kill/wipe or timed/depleted result, boss HP% at wipe.
- Hardware-encoded on NVIDIA (NVENC), AMD/Intel (VAAPI), or software.
- Opt-in content filters (e.g. record Mythic raids only, skip low keys, ignore
  battlegrounds), an optional pre-encounter buffer (`--preroll-seconds`) and
  post-encounter tail (`--postroll-seconds`), and disk-space auto-cleanup of the
  oldest non-favorited clips.
- Manual Record / Stop from the GUI, a desktop notification on each saved clip,
  and a warning when WoW's Advanced Combat Logging is disabled.

**Privacy — chat-overlay masking.** Optionally blacks out a configured screen
region (a panel branded with the Wipelog logo) so chat, whispers, and
guild/member names are hidden from clips you share. Run it automatically on
every clip, or on-demand per pull via right-click → *Create masked copy for
sharing*. Default off. See [`docs/chat-overlay.md`](docs/chat-overlay.md).

**Library + GUI (Avalonia).** Browse, filter, play, tag, note, and curate clips
backed by a shared SQLite library:

- A per-content sidebar (Mythic+, Raids, Arena, Solo Shuffle, Battlegrounds,
  Clips), plus search, date-range, and favorites filters.
- LibVLC player with a synced timeline of party deaths and boss starts (from
  each clip's `.events.json`), a content-aware details panel, and a raid-comp
  chip strip (🛡 / ✚ / ⚔ / 💀).
- Per-clip favorite, delete, edit-metadata, and free-form notes; multi-select
  bulk delete and `.zip` archive with a cancellable progress dialog.

See [`docs/architecture.md`](docs/architecture.md) for the phase-by-phase
project history.

## Prerequisites

- .NET 10 SDK (10.0.x)
- Linux (X11; Wayland needs XWayland)
- `gpu-screen-recorder` on PATH — install via flatpak
  (`flatpak install flathub com.dec05eba.gpu_screen_recorder`) or your
  distro's package manager
- `ffmpeg` / `ffprobe` on PATH — required for the pre/post-roll buffer and
  chat-overlay masking (a full build with NVENC/VAAPI encoders for hardware
  re-encode); most distro `ffmpeg` packages include them
- `libvlc` system package for the GUI's embedded playback:
  - Arch: `sudo pacman -S vlc`
  - Debian/Ubuntu: `sudo apt install vlc`
  - Fedora: `sudo dnf install vlc`

## Build

```sh
dotnet build
```

## Test

```sh
dotnet test
```

## Publish

```sh
./publish.sh
```

Builds self-contained linux-x64 single-file binaries to `dist/daemon/`
and `dist/app/`. Native libraries (SkiaSharp, SQLite, …) are bundled
and self-extracted at startup, so the only runtime dependency is the
system `libvlc`.

## Install

Pick one of the two layouts; the systemd unit and the `.desktop`
entry expect the binaries at one of these paths.

```sh
# Option A — self-contained (no .NET runtime needed on the host)
./publish.sh
mkdir -p ~/bin/wipelog ~/bin/wipelog-app
cp -r dist/daemon/. ~/bin/wipelog/
cp -r dist/app/.    ~/bin/wipelog-app/

# Option B — framework-dependent (needs .NET 10 runtime, smaller, faster to iterate)
dotnet publish src/Wipelog.Daemon -c Release --no-self-contained -o ~/bin/wipelog
dotnet publish src/Wipelog.App    -c Release --no-self-contained -o ~/bin/wipelog-app
```

Add `~/bin/wipelog` and `~/bin/wipelog-app` to your `PATH` (or call
the binaries by absolute path). After this, the GUI's
**Settings → Systemd Service → Install service** wires the daemon
up as a per-user service.

## Run the daemon

For day-to-day use, install the systemd user service via the GUI (see
the next section) — it auto-starts on login and restarts on failure.
For ad-hoc sessions:

```sh
Wipelog.Daemon \
    --log-dir "<path-to-WoW>/_retail_/Logs" \
    --output  "$HOME/Videos/wow"
```

In WoW, run `/combatlog` once at the start of each session — that creates the
`WoWCombatLog-*.txt` file the daemon tails.

### Run as a systemd user service

The simplest path is **Settings → Systemd Service → Install service**:
the GUI generates `~/.config/systemd/user/wipelog.service` from the
paths + encoder options you've filled in, runs `daemon-reload`, and
enables the unit. **Reinstall with current settings** rewrites the unit
and restarts the service after you change any config; **Uninstall**
disables and removes the file.

If you'd rather drop the unit in by hand, see
[`packaging/README.md`](packaging/README.md) — same outcome, manual
steps.

## Run the GUI

```sh
Wipelog.App
```

The app reads from `~/.local/share/wipelog/clips.db` (or `$XDG_DATA_HOME/wipelog/clips.db`).
The daemon writes to that DB after each finalized clip; the indexer reconciles on startup so a
manually-deleted file or an out-of-band copy is picked up automatically. The "Re-index library"
button in Settings reruns the reconcile manually.

The title bar shows a daemon-status pill (Recording / Idle / Offline) sourced
from the daemon's heartbeat file. Clicking it opens a popover with
Start / Stop / Restart / Logs buttons that shell out to `systemctl --user`.
Closing the main window hides it to the tray (toggleable in Settings → Window);
Ctrl+Q or the tray "Quit" item shuts down the GUI without affecting the daemon.

On GNOME, system tray support requires the AppIndicator extension. KDE Plasma
and XFCE work out of the box. Wayland support varies — recent Plasma is fine,
GNOME under Wayland requires the same extension.

## Daemon CLI

| Flag | Default | Notes |
|---|---|---|
| `--log-dir` | (required, mutually exclusive with `--log-path`) | Watch a directory; auto-tail newest matching file |
| `--log-path` | (alternative to `--log-dir`) | Tail one specific file |
| `--log-pattern` | `WoWCombatLog-*.txt` | Pattern for `--log-dir` mode |
| `--output` | (required) | Clip output directory |
| `--db-path` | `~/.local/share/wipelog/clips.db` | SQLite DB path |
| `--executable` | `gpu-screen-recorder` | Recorder binary (path or PATH-resolved) |
| `--window` | `focused` | `focused`, `portal`, `region`, or a monitor name |
| `--framerate` | `60` | |
| `--codec` | `h264` | Also `hevc`, `av1` |
| `--audio` | `default_output` | PipeWire/Pulse source |
| `--container` | `mp4` | |
| `--video-quality` | `medium` | `medium`, `high`, `very_high`, `ultra` (gsr presets) |
| `--preroll-seconds` | (off) | Pre-encounter buffer: keep the last N seconds (2–86400) in RAM and prepend them to each clip (needs ffmpeg). |
| `--postroll-seconds` | (off) | Post-encounter tail: keep recording N seconds (0–300) after the encounter ends. |
| `--player-name` | (auto-detect) | Optional hint. The daemon normally identifies the recording player from the combat-log AFFILIATION_MINE bit (only your own events carry it). When that bit is missing the resolver falls back to a diacritic-/case-insensitive name match against this hint, then to a frequency heuristic. Realm and region are derived from the source name (`Name-Realm-Region`) — no separate flag needed. |

### Recording quality vs file size

Approximate, h264 at 1440p60 — actual size depends on scene complexity:

| Quality   | ~MB/min | 30-min M+ |
|-----------|---------|-----------|
| `medium`  | ~110    | ~3.3 GB   |
| `high`    | ~180    | ~5 GB     |
| `very_high` | ~230  | ~7 GB     |
| `ultra`   | ~280    | ~8 GB     |

Default is `medium` — visually fine for review.

### Subcommands

Run with a subcommand instead of live mode:

- `index --output <dir> [--db-path <path>]` — reconcile the SQLite library with
  the on-disk sidecars without starting the recorder (also runs on startup).
- `backfill-events --output <dir> --log-dir <dir> [--force]` — generate (or
  regenerate) `.events.json` sidecars for existing clips from the combat logs
  (see [Backfilling old clips](#backfilling-old-clips)).
- `apply-overlay --clip <file> [--output <file>] [--in-place] [--x N --y N
  --width N --height N]` — apply the chat-overlay mask to an existing clip
  (region defaults from `preferences.json`; writes a sibling `*.masked.<ext>` by
  default). Handy for checking the region and hardware-encode timing without
  recording.

## GUI layout

The window is split into three columns:

- **Left sidebar (220 px)** — three stacked sections: a daemon
  **Status** pill (offline / idle / recording with a pulsing dot for
  the recording state); a **Recordings** group with six entries
  (Mythic+, Raids, Arena, Solo Shuffle, Battlegrounds, Clips) each
  carrying a count pill; and a **Settings** group docked to the bottom
  with General (opens the full-page settings overlay) and Scene
  (placeholder). Keyboard shortcuts `1` - `4` jump to Mythic+ / Raids /
  Battlegrounds / Clips; Arena and Solo Shuffle aren't bound to digits
  yet — click them in the sidebar.
- **Center column** — the LibVLC video surface on top, then a unified
  **scrub-bar-with-tombstones** widget (yellow encounter regions on
  the bar, boss-name labels above 🪦 glyphs below, "+N" overflow
  badges for tight death clusters), then transport controls
  (Play/Pause · ◀10 · 10▶ · time · volume), then the **filter bar**
  (Deaths segmented control · Encounters · ★ favorites · Search ·
  Date range), and finally the sortable `DataGrid` of clips.
- **Right details panel (300 px)** — content-type-aware metadata for
  the selected clip: Started / Duration / Size always-on, plus per-type
  rows (Encounter + Difficulty + Instance for raids, Dungeon + Keystone
  for M+, Bracket for Arena / Solo Shuffle, Map for Battlegrounds), and
  Character / Spec when known. A raid-comp chip strip (🛡 tanks /
  ✚ healers / ⚔ DPS / 💀 deaths) above the grid is fed by the
  `.events.json` sidecar's `combatants` list and hides itself when no
  data is available.

Clicking a row in the table drives the player, the scrub bar's
events, and the details panel. The favorite ★ shows in the row's
first cell; right-click for Delete / Edit metadata / Copy file path.
Ctrl/Shift-click to multi-select — the table status footer flips to
a `[Delete] [Archive] [Clear]` action bar; archive streams the
selection into a single `.zip` (mp4 stored, sidecars deflated) with a
cancellable progress dialog.

The clip table swaps column sets per sidebar category. Today there
are three variants — Mythic+ and Raid have dedicated layouts, and
everything else (Arena, Solo Shuffle, Battlegrounds, Clips) falls
through to the generic "All" variant:

| Sidebar category | Columns |
|---|---|
| Mythic+ | Dungeon · Key · Result · POV · Duration · Date |
| Raids | Encounter · Difficulty · Result · POV · Duration · Date |
| Arena / Solo Shuffle / Battlegrounds / Clips | Content · Title · Result · POV · Duration · Date |

The **POV** cell shows a square in the character's class color
(canonical Blizzard hex) followed by *Name · Spec Class*. The
content-specific metadata (Bracket, Map, …) lives in the right-hand
details panel rather than as table columns.

## GUI keyboard shortcuts

Press `?` (Shift + /) in the app for the full live list. Summary:

**Player** (suspended while a text input has focus)

| Key | Action |
|---|---|
| Space | Play / pause |
| J / L | Skip back / forward 10s |
| Left / Right | Skip back / forward 5s |
| Shift+Left / Shift+Right | Skip back / forward 30s |
| , / . | Step one frame back / forward |
| 0, 4 - 9 | Seek to 0%, 40%, … 90% of duration |
| Home / End | Seek to start / near end |
| Up / Down (or + / -) | Volume +5 / -5 |
| M | Mute toggle |

**Table** (when the clip table has focus)

| Key | Action |
|---|---|
| ↑ / ↓ (or J / K) | Move the highlight to the previous / next clip (doesn't load yet) |
| Enter | Play the highlighted clip |

**Timeline** (suspended while a text input has focus)

| Key | Action |
|---|---|
| T | Cycle Deaths filter: All → Own → None |
| B | Toggle encounter regions + boss-start labels |
| Click a tombstone | Seek back 5s and pause (configurable in Settings) |
| Click a boss label | Seek to encounter start (no pause) |

**Global**

| Key | Action |
|---|---|
| 1 / 2 / 3 / 4 | Switch sidebar: M+ / Raids / Battlegrounds / Clips |
| F | Toggle favorite of selected clip |
| Delete | Delete selected clip (with confirmation) |
| Ctrl+F | Focus search box |
| Ctrl+L | Focus clip table |
| Ctrl+Q | Quit the app (bypasses close-to-tray) |
| Esc | Clear search if focused, else unfocus |
| ? | Show keyboard shortcuts dialog |

**Clip list context menu** (right-click a clip)

| Item | Action |
|---|---|
| Play | Open the clip in the player |
| Edit metadata… | Override player name/realm/class/spec, kill/wipe |
| Create masked copy for sharing… | Mask the chat region into a new copy in the Clips tab (original untouched) |
| Copy file path | Copy the .mp4 path to the clipboard |
| Delete… | Delete the clip + sidecar (with confirmation) |

## Events sidecar (`<clip>.events.json`)

Every clip the daemon records is accompanied by a JSON file with the same
stem and a `.events.json` extension. It carries timeline-relevant combat
events captured during the recording window:

- **Player deaths** (party + raid, not pets/NPCs), with the killing-blow
  source and spell name when available — looked up from a 5-second rolling
  window of damage events ending at each death.
- **Boss starts inside the recording** (M+ inner bosses). Raid clips have
  an empty list here because the encounter that triggered the recording
  itself is intentionally excluded — it would be a duplicate of the clip
  header.

The format is versioned (`version: 2` today — v2 adds a top-level `combatants`
list with each player's class/spec/role for the raid-comp chip strip). Readers
should ignore unknown `kind` values rather than failing, and older v1 readers
treat the absent `combatants` field as empty.

The clip library tracks a `has_events` flag derived from filesystem
presence — the indexer flips it on/off automatically. Hand-deleting a
`.events.json` correctly clears the flag on the next index pass.

### Backfilling old clips

For clips recorded before this stage existed, run:

```sh
Wipelog.Daemon backfill-events \
    --output  "$HOME/Videos/wow" \
    --log-dir "<path-to-WoW>/_retail_/Logs"
```

The backfiller scans clips lacking `.events.json`, replays log lines that
fall in each clip's `[startedAt, endedAt]` window through the same buffer
the live recorder uses, and writes the sidecar. If the relevant log files
have rotated away the clip is skipped silently — those events are gone.
The DB's `has_events` flag is reconciled at the end of the run.

Pass `--force` to regenerate every existing `.events.json` from the
current parser+buffer code (useful after a parser fix lands):

```sh
Wipelog.Daemon backfill-events --force \
    --output  "$HOME/Videos/wow" \
    --log-dir "<path-to-WoW>/_retail_/Logs"
```

`--force` only overwrites the `.events.json`; it never touches the
`.mp4` or main `.json`. Clips whose log window has rotated away are
still skipped silently — the existing (stale) sidecar stays as-is.

## Timeline

The GUI shows a ribbon below the player video, populated from each
clip's `.events.json`. The ribbon doubles as the scrub bar: click
empty space to seek, drag to scrub, click a marker for its
seek-back-and-pause shortcut.

- **Death markers** (skull glyph) with the first-3-chars of each
  character's name above the glyph. Your own deaths render in bright
  red, party members' in muted red. Toggle "All party deaths" off
  (or press `T`) to show only yours.
- **Two-row stacking.** When labels would overlap (5+ deaths in a
  10-second window — common in raid wipes), the second death goes on
  a second row instead of cramming horizontally. Beyond that, the
  rest collapse into a small "+N" badge whose tooltip lists the
  hidden deaths.
- **Boss-start bars** with the encounter name below the bar. Only
  appears in M+ clips; raids have an empty boss track because the
  encounter that triggered the recording is the boss fight itself.
  Toggle with `B`. If two boss labels would overlap, the earlier one
  is truncated or hidden.
- A **playhead cursor** synced to the video position.

**Click a marker** to seek back 5 seconds and pause (both values are
configurable in *Settings → Timeline*); clicking the "+N" badge does
the same against the first death in the cluster. **Hover** to see the
wall-clock time, the player name, and the killing-blow source/spell
when known.

If a clip has no `.events.json` (e.g. it was recorded before the
events sidecar landed), the timeline area shows a muted "No events
captured for this clip" hint with a pointer to `backfill-events` —
you can regenerate sidecars for old clips as long as the original
combat log is still on disk.

## Editing clip metadata

Right-click any clip in the list and pick **Edit metadata…** to override:

- player name, realm, class, spec
- kill / wipe (or timed / depleted for M+)

Useful for backfilling clips recorded before `--player-name` was correct,
fixing a misdetection, or correcting a result that the daemon got wrong
(e.g. an encounter end the parser misread). Changes write to the SQLite
library and the JSON sidecar; the indexer keeps them in sync on next run.

The dialog can only set or replace fields, not clear them — to wipe a
field, edit the JSON sidecar directly and rerun the index command.

## Troubleshooting

**Character / Spec dropdowns are empty for a recent clip.** Check the
daemon logs around the recording for `Could not resolve recording player`.
If the buffer summary shows your character with very few source events
(e.g. you were AFK at pull start) the auto-detect path may have picked
another raid member. Use right-click → Edit metadata to correct it.

**Old clips don't have character/spec.** Expected for clips recorded
before player identity resolution landed (or before `--player-name`
was correct). Either right-click → Edit metadata to backfill, or
leave them alone — they remain filterable by everything else.

## Layout

```
src/
  Wipelog.Core/      # combat-log parser, file tailer, encounter state machine, recorder wrapper
  Wipelog.Library/   # SQLite + Dapper clip index, indexer
  Wipelog.Daemon/    # generic-host console app
  Wipelog.App/       # Avalonia GUI
tests/
  Wipelog.Core.Tests/
```

See [`docs/architecture.md`](docs/architecture.md) for the planned phases and
how the pieces fit together.

## License

GNU General Public License v3.0 — see [`LICENSE`](LICENSE).
