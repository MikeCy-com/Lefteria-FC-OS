# Public roster and section projections

This slice supplies the typed data foundation for the missing public player pages,
statistics and section views. It does not complete registration, customer accounts,
payments, player evaluations or the remaining admin modules.

## Source and visibility

The original GitHub frontend was inspected at `380b402`; the later original main
`979935267fa2662580579fb33e987b5cc219d969` changes storage/backend behavior. The
source UI and models define eligible published players, Academy surname
abbreviation, staff assignments, recorded statistics and archive scopes. Source
snapshot provenance is supplied explicitly by each import, rather than inferred
from a local checkout or current production state.

`GET /api/public/sections?organization_id=lefteria_fc` or
`lefteria_fc_academy` returns typed anonymous projections with no-store caching.
Invalid organizations return 400; an uninitialized or inconsistent projection
returns 503. Cookie and bearer credentials do not affect public visibility.
Missing statistics remain null; recorded zeroes remain zero.

Academy names are abbreviated before persistence in public projection rows.
Private contact, guardian, medical, account and registration fields remain in
owner-only source evidence. Conflicting archived player scope quarantines the
whole archive instead of publishing a child's surname under the Club scope.
Public images still require separately reviewed publication; raw private media
is not mounted by this slice.

## Import and recovery

The owner CLI supports `--kind public-sections`. Dry-run validates a bounded,
complete 21-collection manifest without constructing a database connection.
The base-public digest and repeated teams/groups/fixtures/club source rows must
match the already imported immutable base snapshot. This capability currently
accepts only development and staging targets.

Snapshot versions are sequential, immutable and transactionally published.
Same-version replay reconciles projection, source, issues and manifest evidence;
historical replay never moves the current pointer backwards. The shared import
advisory lock serializes conflicting writers. All original bytes and quarantined
rows remain recoverable through owner-only tables.

Apply `20261101202955_PublicSections` with the explicit owner migration command,
then reapply the documented application grants. The runtime can read typed
projections and limited readiness columns, but cannot access source evidence or
mutate publications. Empty Down/Up preserves the older public-content FK.
Populated Down refuses with `public_sections_data_requires_forward_recovery`;
use a reviewed forward correction or restore a verified backup into an isolated
recovery database. No schema or public-sections data has been deployed to
Hostinger as part of the earlier editorial layout release.

## Verification

`--public-sections-policy` exercises eligible players, unknown statistics,
Academy profile/archive privacy, malformed data and preserved source evidence.
`--public-sections-fixture <identity-db.json>` exercises real HTTP and PostgreSQL:
concurrent import/replay, exact organizations, runtime privilege denials,
historical publication, tamper refusal, empty Down/Up and populated rollback
refusal. Both passed on the guarded disposable local fixture on 2026-11-02.
The same HTTP case is included in the existing full integration suite.
