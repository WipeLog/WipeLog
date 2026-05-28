# Wipelog documentation

Wipelog is a Linux-native World of Warcraft gameplay recorder. A long-running
daemon tails the WoW combat log, drives `gpu-screen-recorder` to capture
encounters, and writes clip metadata to a shared SQLite library. A separate
Avalonia GUI reads that library and plays the clips back with a synced
timeline of deaths and boss starts.

The reference for "what does this code do, why" is [`../CLAUDE.md`](../CLAUDE.md).
The docs in this folder are everything else: system overview, on-disk
formats, contributor onboarding, troubleshooting, and per-area audit
snapshots.

## Contents

| Document | What's in it |
|---|---|
| [`architecture.md`](architecture.md) | System overview (two-process model, IPC, data flow) plus the phase-by-phase project history. |
| [`data-formats.md`](data-formats.md) | On-disk formats: clip sidecar JSON, `.events.json`, SQLite schema and migrations, `preferences.json`, daemon `status.json` heartbeat. |
| [`daemon.md`](daemon.md) | What the daemon does, full CLI reference, dev workflow (running it by hand, log locations, redeploy). |
| [`chat-overlay.md`](chat-overlay.md) | Chat-region privacy masking: enabling it, encoder tiering (NVENC/VAAPI/software), performance by resolution, the `apply-overlay` test command, troubleshooting. |
| [`gui.md`](gui.md) | Avalonia view hierarchy, MVVM patterns, LibVLCSharp playback, library watcher, timeline rendering. |
| [`contributing.md`](contributing.md) | Build / test / publish commands, commit conventions, project boundaries, where each kind of change belongs. |
| [`troubleshooting.md`](troubleshooting.md) | Known gotchas the project has hit and worked around — symptom, cause, workaround. |
| [`audit-core.md`](audit-core.md) | Per-area code-review snapshot of `Wipelog.Core`. |
| [`audit-daemon.md`](audit-daemon.md) | Per-area code-review snapshot of `Wipelog.Daemon`. |
| [`audit-library.md`](audit-library.md) | Per-area code-review snapshot of `Wipelog.Library`. |
| [`audit-gui.md`](audit-gui.md) | Per-area code-review snapshot of `Wipelog.App`. |

## Audience

The project [`README.md`](../README.md) is the entry point for someone
discovering Wipelog on GitHub. The documents in this folder are for someone
who's decided to use Wipelog, debug it, or contribute. They assume you've
read the README, can build the solution, and want detail rather than pitch.
