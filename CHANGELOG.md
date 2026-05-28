# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial public release of Wipelog: a Linux-native World of Warcraft
  gameplay recorder.
- Daemon that tails the WoW combat log and drives `gpu-screen-recorder`
  to capture raid encounters, Mythic+ runs, Arena, Solo Shuffle, and
  Battlegrounds. Hardware-encoded on NVIDIA (NVENC), AMD/Intel (VAAPI),
  or software.
- Avalonia GUI for browsing, filtering, playing, tagging, and curating
  clips. LibVLC-backed playback with a synced timeline of party deaths
  and boss starts.
- Per-content sidebar (Mythic+ / Raids / Arena / Solo Shuffle /
  Battlegrounds / Clips) with search, date-range, and favorites
  filters; sortable DataGrid; bulk delete and zip archive.
- Content recording filters (record Mythic raids only, skip low keys,
  ignore battlegrounds) with hot-reload — no daemon restart needed.
- Pre-encounter buffer (`--preroll-seconds`) and post-encounter tail
  (`--postroll-seconds`); disk-space auto-cleanup of the oldest
  non-favorited clips.
- Chat-overlay masking: optionally blacks out a configured screen
  region (chat, whispers, names) on saved clips. Run automatically
  or on-demand per pull via right-click → *Create masked copy for
  sharing*.
- Manual Record / Stop from the GUI via a Unix-domain control socket.
- First-run onboarding: a "Detect WoW logs" button that scans common
  Linux WoW launcher prefixes (Lutris, Heroic, Bottles, raw Wine).
- Warning when WoW's Advanced Combat Logging is disabled.
- systemd user service for auto-start on login; GUI installer that
  generates the unit from current Settings.

[Unreleased]: https://github.com/WipeLog/WipeLog/compare/HEAD...HEAD
