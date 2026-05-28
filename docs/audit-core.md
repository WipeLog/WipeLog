# Audit: `Wipelog.Core`

> **Note.** This is a snapshot from a per-area code-review pass.
> Specific line numbers and references to commit hashes may not match
> the current tree — `git log` and `git blame` are authoritative.

## Summary

Core is the healthiest area of the codebase. The pure-logic surface
(combat-log parser, encounter detector, identity resolver, recording
event buffer) is well-tested and has clean separation from I/O.
`RecorderController` and `GpuScreenRecorder` are the orchestration
layer and carry essentially all of Core's mutable state and external
side effects — they're long and they're the right place for the
defensive `catch (Exception)` patterns, but their size is worth
flagging as a future-pain risk.

## Critical findings

No critical findings.

## Important findings

### `RecorderController.cs` at 842 lines is the orchestration hot spot

[`src/Wipelog.Core/Recording/RecorderController.cs`](../src/Wipelog.Core/Recording/RecorderController.cs)
holds the per-event loop, the identity buffer, the recording event
buffer wiring, the pre-roll lifecycle, the finalize step, and the
ffmpeg-concat path. Sixteen `catch (Exception)` blocks live here —
every one is a deliberate log-and-continue around a best-effort I/O
operation (recorder start, file move, observer notification, pre-roll
flush). That pattern is the right call for this code, but the file's
scope means a single change has to be reasoned about against a wide
state machine.

**Why it matters.** Future bugs that surface here (especially around
the buffer / finalize interaction — see the `4ca988c` kill-blow fix in
the commit history) are easy to introduce and hard to spot in review.
The file is also the only place that "knows" the full lifecycle, so
new content types or new sidecar fields require edits in three or four
methods that are far apart.

**Suggested direction.** Split is plausible — the pre-roll sub-machine
and the finalize step could each be a private class — but it's not
urgent. A near-term win that costs less: add a comment block at the top
of the file naming the public entry points (`RunAsync`,
`StartManualAsync`, `StopCurrentAsync`) and the invariants the
`_stateLock` is protecting, since right now the locking discipline is
distributed across the file.

### Encounter detector TODO on RBG detection

[`src/Wipelog.Core/Detection/EncounterDetector.cs:45-46`](../src/Wipelog.Core/Detection/EncounterDetector.cs)
carries a TODO that calls out a real ambiguity: the
`BattlegroundZoneIds` set today merges Rated and Unrated BGs because
nobody has confirmed whether `ZONE_CHANGE` ID differs between the two.
The filter VM folds both into one rail tab as a result.

**Why it matters.** Wipelog already exposes a `RatedBattleground`
`ContentType` value, but no detection path actually emits it. The DB
schema, sidecar format, and rail UI are ready; the daemon doesn't
have the signal. Until a real RBG combat log lands in
`tests/Wipelog.Core.Tests/Fixtures/`, users on the rated bracket get
their clips bucketed as plain Battlegrounds.

**Suggested direction.** Either close the gap (capture an RBG log,
confirm whether the secondary signal lives in `PVP_MATCH_START`,
`COMBATANT_INFO`, or somewhere else, and route the type) or remove
`RatedBattleground` from `ContentType` until there's a path that
populates it. Half-implemented values rot.

### Solo Shuffle / Battleground win-loss

`Wipelog.Core.Recording.RecorderController.ResolveArenaOutcome` exists
for Arena and Solo Shuffle, but only the 2v2 / 3v3 / Arena Skirmish
branch produces `_win.mp4` / `_loss.mp4`. Solo Shuffle keeps the
placeholder `_complete.mp4` suffix because there's no single signal
(it has six rounds with rotating partners). BGs keep `_complete` for
the same reason — `ARENA_MATCH_END` doesn't fire.

**Why it matters.** `Success` on the DB row is false-ish for clips
the user actually won. Filtering by "won runs only" is broken for
those content types, and the filename doesn't tell the user the
outcome.

**Suggested direction.** Either compute outcome from the score table
in `PVP_MATCH_END` (if it exists in the combat log — verify against a
real fixture) or surface the partial signal we *do* have (e.g. round
wins for Solo Shuffle) as a separate metadata field rather than the
binary `Success` bit. Out of scope for an audit; flagged so future
work doesn't paper over it.

## Nice-to-have findings

### Pure logic in `Detection/` and `CombatLog/` has zero broad catches

A positive nice-to-have: `EncounterDetector`, `CombatLogParser`,
`PlayerSpecResolver`, `LogTailer`, and the string normalisation helper
all use specific exception types or no exception handling at all. Worth
preserving — if new pure-logic code lands in these directories,
match the local style rather than copying the `RecorderController`
defensive pattern.

### Damage suffix offset could be a named constant

`BuildDamageEvent` in `CombatLogParser.cs` counts the last 10 fields
as the damage suffix. The number is referenced in CLAUDE.md's "Damage
event parsing" section, but lives in the parser as a literal `10`.
Naming it (e.g. `private const int DamageSuffixFieldCount = 10;`)
would make the locality of the assumption explicit. Trivial; flagged
because new contributors burning time reading the parser is exactly
what comments-as-named-constants prevent.

## Observations (not findings)

### Is the broad-catch pattern in `RecorderController` deliberate?

The 16 `catch (Exception)` blocks in
[`RecorderController.cs`](../src/Wipelog.Core/Recording/RecorderController.cs)
are uniformly "log and continue around a best-effort side effect."
Examples:

- `RecorderController.cs:165` — pre-roll start fails; record without
  pre-roll.
- `RecorderController.cs:271` — identity resolver throws; record
  without identity.
- `RecorderController.cs:382` — lifecycle observer throws; the
  daemon should not die because a subscriber misbehaved.
- `RecorderController.cs:733` — file rename fails after ffmpeg concat;
  keep the original clip.

That pattern is the right call here — the recorder is a long-running
loop and an exception inside it would tear down the host process. The
question is whether this is **documented** as the convention or
**inherited** by copy-paste. If the former, a short comment at the
top of the file explaining the pattern would prevent a future
contributor from "fixing" one of these into a typed catch and
accidentally introducing a crash. If the latter, that contributor is
already on a collision course.

### Single test project with project refs to all four

`Wipelog.Core.Tests` references `Wipelog.Core`, `Wipelog.Library`,
`Wipelog.Daemon`, and `Wipelog.App`. Tests for types in any of those
projects live in this one test project. That keeps the friction of
adding a test low — there's no decision tree for "which test
project" — but it also means a build of the test binary pulls every
production dependency including Avalonia and LibVLCSharp.

If this is deliberate (the project is small enough that the unified
test project is worth the coupling), it deserves a short note in
[`contributing.md`](contributing.md) so a future contributor doesn't
"organise" the tests into per-project test projects without
understanding the trade-off. If it's accidental, splitting `*Tests`
into per-area projects (`Wipelog.Library.Tests`, `Wipelog.App.Tests`,
etc.) is the standard refactor.

## Tests

### Coverage by subdirectory

| Subdirectory | Test coverage |
|---|---|
| `CombatLog/` | Strong: `CombatLogParserTests`, `CombatLogFieldSplitterTests`, `LogTailerTests`, `DamageEventParserTests`, `CombatantInfoParseTests`. |
| `Detection/` | Strong: `EncounterDetectorTests`, `PlayerSpecResolverTests`, `StringNormalizationTests`. |
| `Recording/` | Strong: `RecorderControllerTests`, plus three focused suites (`RecorderControllerStopTests`, `RecorderControllerPreRollTests`, `RecorderControllerIdentityTests`, `RecorderControllerAclObserverTests`), `GpuScreenRecorderTests`, `PreRollRecorderTests`, `RecordingEventBufferTests`, `FfmpegConcatTests`, `DefaultClipNamingStrategyTests`, `TimelineEventsRoundTripTests`. |
| `Data/` | Adequate: `ClassColorsTests`, `DungeonTimersTests`, `SpecMapTests`. No tests for `RaidInstances` or `SpecIcons` — both are largely static lookup tables; the risk of regression is low. |
| `Preferences/` | Covered by `UserPreferencesServiceTests`. |
| `Status/` | Covered indirectly by `HeartbeatWriterTests` (Daemon) and `DaemonStatusViewModelTests` (App). |
| `Control/` | Covered indirectly by `ControlServerTests` (Daemon). No direct test of `ControlSocketPath.Resolve` against `XDG_RUNTIME_DIR` vs the `/tmp` fallback. |
| `Time/` | No direct tests. `FakeClock` (the test double) exists and is exercised everywhere `IClock` is injected, so the production `SystemClock` is implicitly covered — but a one-line `SystemClock.Now` smoke test would close the loop. |

### Quality

The Recording/ test split into four `RecorderController*Tests` files
covers four genuinely different state-machine paths (stop, pre-roll,
identity, ACL observer) without making any individual test file
unreadable. That's the right shape. The fake recorder scripts in
[`tests/Wipelog.Core.Tests/Fixtures/`](../tests/Wipelog.Core.Tests/Fixtures/)
let the tests exercise the gpu-screen-recorder integration without
actually running it — also the right shape.

No brittleness flags: tests use `TestContext.Current.CancellationToken`,
`FakeClock`, and `TempDirectory` consistently. Cancellation tokens are
propagated rather than swallowed.

### Gaps worth filling

- A direct `ControlSocketPath.Resolve` unit test for the `XDG_RUNTIME_DIR`
  vs `/tmp/wipelog-{uid}` fallback. Today's coverage is end-to-end via
  `ControlServerTests`, which doesn't exercise the fallback path.
- A `SystemClock` smoke test (one assertion that `Now` returns a
  recent `DateTimeOffset`). Trivial; closes the implicit-only gap.
- An RBG-flavoured combat-log fixture and matching detector test once a
  real one is captured (see the TODO at
  [`EncounterDetector.cs:45`](../src/Wipelog.Core/Detection/EncounterDetector.cs)).
