# Audit: `Wipelog.Library`

> **Note.** This is a snapshot from a per-area code-review pass.
> Specific line numbers and references to commit hashes may not match
> the current tree — `git log` and `git blame` are authoritative.

## Summary

Library is the smallest project (≈ 1,100 LOC across 6 files) and the
most focused: a Dapper-over-SQLite repository, a migration runner, and
a filesystem indexer. WAL mode and atomic upserts are the right
foundations for concurrent daemon-and-GUI access. Two concerns worth
naming: `ClipRepository.cs` at 562 lines is dominated by hand-written
SQL strings that aren't shared across methods, and the indexer has
no explicit handling for sidecar version drift.

## Critical findings

No critical findings.

## Important findings

### `LibraryIndexer` doesn't gate on sidecar version

[`src/Wipelog.Library/LibraryIndexer.cs`](../src/Wipelog.Library/LibraryIndexer.cs)
deserialises every `<stem>.json` into a single `SidecarDto` shape and
calls `ToRecord` to project it to a `ClipRecord`. There's no read of
the sidecar's `version` field — newer fields are simply absent from
the DTO and quietly become null on the record, older fields are
read regardless of version.

This is fine *today* because v2 of the sidecar is a strict superset
of v1. It will not be fine the moment a field changes meaning across
versions (e.g. an enum value renamed) — the indexer would silently
mis-categorise old clips against new semantics.

**Why it matters.** The version field exists precisely to gate
behaviour across schema changes. Not reading it means a future
breaking sidecar change will produce data corruption on first
reconcile rather than a loud failure.

**Suggested direction.** Read `dto.Version` in `ToRecord` and switch
on it: v1 sidecars take the v1 path, v2 the v2 path. Today both
paths are identical and the switch is a no-op, but its presence
captures the convention and forces the next change to either match
or be explicit about why it doesn't.

### `ClipRepository.cs` at 562 lines is mostly hand-written SQL

[`src/Wipelog.Library/ClipRepository.cs`](../src/Wipelog.Library/ClipRepository.cs)
contains every clip query, insert, update, delete, tag operation,
bulk operation, and statistics query as a distinct method, each with
its own SQL string literal. The same SELECT column list is repeated
several times. The `QueryClipsAsync` method builds dynamic WHERE
clauses by string concatenation with conditional appends.

This is not a defect per se — Dapper's whole point is "write the SQL
you want" — but the file shape makes it easy to forget a column when
a new field lands (see the audit-daemon finding about `LibrarySink`
for the matching problem on the write path), and the dynamic WHERE
builder in `QueryClipsAsync` is the kind of code where SQL injection
risks accumulate even with parameterised inputs.

**Why it matters.** Schema migrations 002-008 each touched
`ClipRepository`. Each one had to update the SELECT list, the upsert
column list, and the model mapping in three places that don't share
code. The number of places will keep growing.

**Suggested direction.** Two low-cost moves: (a) factor the
`clips.*` column list into a single `ClipColumns` constant referenced
by every SELECT; (b) test the dynamic WHERE builder with a query
expressing every filter at once, asserting the produced SQL matches
the parameter list. Neither requires switching ORMs.

## Nice-to-have findings

### `Models.cs` aggregates several public records

[`src/Wipelog.Library/Models.cs`](../src/Wipelog.Library/Models.cs)
holds `ClipRecord`, `ClipQuery`, and friends in 81 lines. That's fine
right now; flagging because every new field on `ClipRecord` (and there
have been many) means a multi-file diff (`ClipRecord` definition,
`ClipRepository` SELECT, `ClipRepository` INSERT/UPDATE, sidecar DTO,
`LibrarySink` mapping). Splitting `Models.cs` into per-record files
would not reduce that surface area; the actual win would come from
generating the mapping. Out of scope as an audit recommendation;
recorded so the next time someone reaches for a code generator they
have prior art.

### `TagNormalization.cs` is exercised by tests but not by code paths

[`src/Wipelog.Library/TagNormalization.cs`](../src/Wipelog.Library/TagNormalization.cs)
is a 44-line file with `TagNormalizationTests` covering it. Grepping
the rest of the codebase shows it's used by `ClipRepository`'s tag
methods — so it is exercised. No action needed; this is a positive
"the test suite reflects production paths" observation.

## Observations (not findings)

### Migration runner uses string-prefix matching on resource names

[`ClipDatabase.cs:127-140`](../src/Wipelog.Library/ClipDatabase.cs)
loads migrations by reflecting on embedded resource names and
matching `Migrations.{NNN}_`. A reordered file (`002_a.sql` and
`002_b.sql`) would silently pick whichever the reflection ordering
returns first.

Is this intentional ("one migration per number, enforced by
convention")? If yes, a one-line assertion at the top of the loop —
"`assert exactly one resource matches`" — would catch a bad rename
the moment a developer hits it instead of after a confusing partial
migration in production. If not, switching the matcher to require a
full filename would close the gap.

### Bulk delete is best-effort across three files per clip

`IClipRepository.DeleteClipsAsync` runs a single SQL transaction
across selected clips, then sweeps each clip's three on-disk files
(video + main sidecar + events sidecar) outside the transaction. If
the file sweep fails for one clip, the DB row is gone but the files
remain. CLAUDE.md flags this as a deliberate choice.

For users on a read-only output dir (snapshots, removable media), this
is the right call — the alternative is leaving orphaned DB rows. But
the behaviour is silent: the user gets no signal that the file
remained. A `LogWarning` for each per-file failure (sum'd in the
return value) would surface it without changing the semantics.

## Tests

### Coverage by area

| Area | Tests |
|---|---|
| Repository CRUD | `ClipRepositoryTests` — covers insert, update (incl. COALESCE upsert semantics), delete, query with filters. |
| Indexer | `LibraryIndexerTests` + `LibraryIndexerV2Tests` — v2 specifically exercises the `has_events` flag flipping when `.events.json` is added or removed. |
| Migrations | `Migration002Tests` — applies migration 002 against an in-memory DB and asserts schema shape. Migrations 003-008 are not directly tested. |
| Tags | `TagNormalizationTests`. |
| Archival | `ClipArchiverTests` (App-side, but operates on the repository's data). |
| Events loader | `ClipEventsLoaderTests` (App-side, reads `.events.json`). |

### Gaps

- **Migrations 003-008**: no per-migration smoke test. `Migration002Tests`
  is the only one; the rest rely on `ClipRepositoryTests` running against
  the fully-migrated schema. A new migration that breaks the
  `user_version` increment would fail the existing tests, but a
  migration that adds a column with the wrong nullability or default
  would pass them.
- **`ClipQuery` dynamic WHERE clause**: tested via individual filter
  inputs, but never with every filter set simultaneously. The
  combinatorial gap is small (six predicates) but real.
- **`ClipRepository.QueryStatsAsync` / count queries**: under-tested
  compared to the main query path. The sidebar's per-type clip counts
  flow through these.

### Quality

`LibraryIndexerV2Tests` is a good example of a test that pins down a
real invariant (`has_events` must follow the filesystem, not the DB)
with a minimal fixture. `ClipRepositoryTests` uses a temp DB file
rather than mocking — appropriate for SQLite. No brittleness or
slow-test flags.
