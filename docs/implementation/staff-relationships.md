# Canonical staff and relationship import

This extension builds on the [canonical roster](canonical-roster.md) and the
[staff source contract](staff-relationship-source-contract.md). It preserves
staff records, team/group assignments, Academy group coach references and
separate player-own, guardian and staff contact provenance. It does not provision
mobile/CMS accounts, select a login role, fill authentication profile links or
grant access to private records.

Staff is a shared person record. A person may have assignments to both FC teams
and Academy groups; there is no invented single-organization constraint on that
person. Each assignment references the existing canonical team/group in its own
organization. Singular and plural source assignment fields are deduplicated with
their provenance retained. A group's explicit coach reference is a staff FK and
also contributes a separately marked group assignment. Job title, team-type
classification and active/archive evidence remain distinct from login authority.

Exact source staff JSON is retained in the owner-only migration schema. Contact
provenance records distinguish missing, null, empty and nonempty source values,
and preserve strings without guessing an international phone prefix or merging
people by phone. Player `phone` and `parent_phone` retain different origins.
Duplicate contact values do not establish an identity or guardian grant. Staff
login summaries and admin pointers remain source evidence for a later account
reconciliation step.

The API receives no new staff, relationship, contact or archive privileges.
Existing player-picker output stays at its safe five-field projection. A future
private roster endpoint must first recheck canonical identity, staff/guardian
status, record ownership and organization scope.

## Explicit input and execution

The existing separate roster tool selects this operation with
`--kind staff-relationships`. Omitting kind preserves the original roster mode.
Normal API startup does not invoke either import.

The manifest fields are schema_version equal to 1, source_repository exactly
MikeCy-com/Lefteria-FC, source_commit as a 40-hex revision, roster_sha256 as the
64-hex digest of the already imported complete roster, complete_collections
exactly `[staff]`, and staff as an array of whole source objects. An intentionally
empty complete staff collection is an empty array. It is different from an
uninitialized import.

Input is bounded to 32 MiB and JSON depth 32. Staff count is at most 5000, IDs
are opaque case-sensitive strings of at most 128 characters, names at most 200,
and each plural assignment list at most 128. Required strings, known source job
titles and team types, status flag types, duplicate IDs/JSON keys, contact bounds
and assignment shapes are validated. There is no silent truncation or skipped
row. Exact raw source objects preserve fields not yet typed by this slice.

Dry run validates the supplied manifest without reading owner credentials or
connecting to PostgreSQL:

    dotnet run --project backend/tools/LefteriaFc.RosterImport -- --kind staff-relationships --input <manifest-path>

Apply additionally requires `--apply`, `--expected-sha256`,
`--expected-database`, `--environment development|staging`, and the server-only
`LEFTERIA_ROSTER_IMPORT_CONNECTION`. The tool verifies the connected database
name and owner authority. Environment remains an operator assertion; these
arguments cannot prove that a host is nonproduction. Independently verify the
actual host, port, server and intended environment before any authorized apply.
No external or production connection is authorized by this implementation.

Apply binds to the existing roster digest and source revision. Player contacts
and group coach references come from that roster's exact owner-only archived
source objects. Existing canonical records and all referenced staff/team/group
IDs must reconcile before a write can commit. An advisory transaction lock
serializes import; records, archives, readiness and append-only audit commit
together. A different snapshot or uninitialized nonempty target is refused.
Exact-digest replay reconciles every persisted staff, assignment, coach, raw
source and contact field against the derived plan, and performs no replacement.
Equal row counts cannot hide changed status, contact values or provenance. This tool
does not overwrite user edits, revoke/reactivate accounts or perform incremental
synchronization.

## Recovery and remaining work

Migration `20261026080850_StaffRelationships` extends the existing roster; it does not edit the
published roster migration. Its rollback removes this extension's staff,
assignments, coach links, contact provenance, source archive and import state.
The underlying roster, mobile identities and profile preferences remain. Keep
the protected original manifests/checksums and an approved backup before rollback;
reapply and explicitly reimport to recover the extension.

Account/credential migration, subject mapping, collision quarantine, current
guardian/player/coach grants, legacy-session bridging, private role APIs and the
operational dashboards remain required follow-up work. A stored relationship is
evidence for that work, not authorization by itself.

## Observed verification — 2026-10-26

All 35 real HTTP/PostgreSQL groups passed against the strictly guarded disposable
loopback fixture. New cases cover strict offline manifest validation; missing
versus verified-empty staff state; bound roster digest/source revision; invalid
references and owner-only apply; archive-failure atomic rollback, concurrent
initial import, exact replay and replacement refusal; shared FC/Academy staff;
distinct own/guardian contacts and no identity grants; runtime denials and safe
picker output. Same-count mutations to staff status, raw source, contacts and
assignment origins are rejected on replay without overwriting them. A direct
SQL regression rejects explicit-empty contact provenance with a null value.

The API, importer, integration suite and preview host passed locked restores and
Release builds with zero warnings/errors. EF reported no pending model changes.
Actual CLI apply/replay, staff-only Down, reapply and reimport preserved the base
roster and a fictional fan's profile/preferences, then restored staff/coach/
contact records. Reapplied restricted-role permission probes passed. Exact
digest/ID cleanup left zero staff/roster/archive/state, identity, avatar and
temporary import-function rows; audits remain append-only. A standalone staff
dry run passed with deliberately invalid import credentials and correctly
reported that the bound roster was not checked.
The documented preview-host HTTP smoke also passed and shut down cleanly.
