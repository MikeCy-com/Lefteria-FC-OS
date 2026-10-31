# Scoped mobile calendar, import and recovery

This slice implements a private calendar for parent, player, coach and management
identities, own-player RSVP writes and staff RSVP reads. Fans have no calendar
grant. All records come from an explicit protected initial import; an empty
screen does not manufacture events or establish that a production export ran.
The [source contract](calendar-source-contract.md) records the original behavior
and remaining compatibility work.

## API and current authorization

All routes require a current rewrite mobile Bearer identity and return
`Cache-Control: no-store`. The legacy Bearer exchange remains a separate boundary.

| Route | Behavior |
| --- | --- |
| `GET /api/mobile/calendar?month=YYYY-MM&player_id=...&offset=...` | Audience-safe month page, at most 200 items, nullable `next_offset` |
| `POST /api/mobile/availability` | Parent/player RSVP for one currently owned and eligible player/event |
| `GET /api/mobile/my-availability` | Direct array of currently owned eligible RSVP rows, at most 500 |
| `GET /api/mobile/calendar/{event_type}/{event_id}/responses?offset=...` | Scoped coach/management player RSVPs, at most 200 players per page |

`event_type` is `match`, its legacy alias `fixture`, `event` or `training`.
It is required in the staff route path. The POST accepts `event_id`, optional
`player_id`, optional `event_type` and `status`. Omitted status defaults to
`going`; explicit null or unknown values are invalid. Accepted statuses are
`going`, `not_going`, `maybe`. Missing event type requires a unique ID across all
three schedule kinds. POST bodies have a 16 KiB limit, strict UTF-8, bounded JSON
depth and duplicate-key rejection. Unknown properties do not establish authority.

Parents choose only among their current active linked children. An omitted
selection uses the current primary child, then a deterministic ID order. Player
mode requires exactly one current linked player. Membership in any assigned
group, an applicable team assignment or an explicit same-organization guest
invitation makes that player eligible. Group assignments take precedence over
team fallback; a truly unassigned event with no guests is organization-wide.
Archived/transferred/inactive players are excluded.

Coaches use current active staff bindings and their full current group/team
assignments. A host-assigned coach can read the event's eligible players in the
coach's scope plus its explicit guests. A coach outside the host scope can see
only invited players from the coach's own scope. Management is limited to the
currently selected organization with an actual current grant. Revoked phone,
role, player, staff, admin, assignment and organization links are rejoined in
the final SQL statement, after the service's initial identity check. Display
team fields, cached role lists and historical grant provenance do not authorize.

Every read and the complete RSVP mutation evaluates its current scope in one
PostgreSQL statement snapshot. This does not add UPDATE privileges to canonical
roster tables for row locks or claim to prevent a revocation committed after
that statement's snapshot.

RSVP atomically upserts separate `availability` and `event_attendance` rows and
appends an actor/session/organization-bound audit. Any failure rolls back all
three effects. It never changes `attendance` physical participation. Cancelled
or archived events reject new RSVPs; past events otherwise remain eligible.
Physical-attendance marking and its past-event locking are not implemented here.

The staff response payload contains only player `id,name,number,position,
image_url` plus RSVP `status,updated_at`. It is not a read receipt, physical
attendance report or MVP contract. The old
`/api/mobile/events/{id}/attendance` route remains unported and is not registered
with this different DTO.

## Dates, publication and browser behavior

Each calendar result declares the manifest's IANA time zone. The importer keeps
date-only values as dates and resolves other values as described in the source
contract. Items have distinct `(kind,id)` identities, day ranges, optional
instants and local schedule times. Month filtering includes a multi-day event
when its audience-specific day range intersects the requested month.

Training has separate staff and member projections/day ranges. A published
snapshot's date and approved member text remain frozen while a staff draft
changes. Draft/review/rejected training without a snapshot is hidden from
members; legacy training exposes schedule fields only. Neither calendar
projection contains private exercise plans or staff notes.

The Svelte calendar has Greek labels, Monday-first week/month navigation,
schedule-kind filters, explicit parent child selection, paging and a separate
staff RSVP panel. Returned calendar days are not shifted through the browser's
local time zone. RSVP refreshes the actual server state after success. Loading,
unavailable and verified-empty states are distinct. Identity, role,
organization and request-revision checks discard late responses after scope
changes. A current-account-unavailable error removes private calendar state
while retaining the existing fan-recovery path. The global shell/header and
role/organization selectors remain shared; unported modules retain the exact
**Under Developments** label.

## Protected complete initial import

`LefteriaFc.RosterImport --kind calendar` is an explicit owner tool, outside API
dependency injection. The schema accepts a UTF-8 JSON manifest no larger than
32 MiB with maximum depth 32. Its fields are:

- `schema_version`: `1`; `source_repository`: `MikeCy-com/Lefteria-FC`.
- `source_commit`: exact 40-hex source revision, matching the imported roster.
- `roster_sha256`: exact 64-hex digest of the protected imported roster manifest.
- `exported_at`: UTC `Z` timestamp; `time_zone`: explicit resolvable IANA ID.
- `complete_collections`: exactly `fixtures,events,training_sessions,availability,
  event_attendance,attendance` in that order.
- An array of full source objects for each of those six collections, including
  explicitly empty arrays. Schedule arrays allow at most 20,000 objects each;
  response arrays allow at most 100,000 each, within the overall byte limit.

Unknown top-level fields, duplicate JSON keys, malformed UTF-8 or missing arrays
reject the envelope before a database adapter is opened. Source objects retain
unknown/private fields in their owner-only archive. Invalid semantic rows,
duplicate IDs/response keys, ambiguous kind references, invalid dates and
cross-organization references are quarantined with fixed issue codes; they are
not discarded or silently mapped elsewhere.

Offline validation checks the envelope and reports `manifest_valid`, with
`bound_snapshots_checked=false`. It does not resolve the roster or promise that
semantic rows will apply. With the owner connection supplied securely through
`LEFTERIA_ROSTER_IMPORT_CONNECTION`, review the actual target first:

    dotnet run --project backend/tools/LefteriaFc.RosterImport --configuration Release -- --kind calendar --input <protected-manifest> --review-target --expected-sha256 <verified-sha256> --expected-database <rewrite-database> --environment staging

Use the same confirmed arguments with `--apply` instead of `--review-target`
only for the intended isolated target. Review mode rolls back without creating
state, source or audit rows. `--review-target` and `--apply` are mutually
exclusive. Environment is restricted to `development` or `staging`; an explicit
matching database and digest are required. Never put credentials or real export
contents in source, command arguments or CI logs.

The owner tool uses the existing import advisory lock, table locks and one
transaction. It reconciles the typed roster against its private source archive,
checks the source commit, exact roster digest and target environment, and
requires an empty calendar target. The six source collections and calendar
must come from one consistent export boundary with that roster. Independently
changed roster data cannot be waved through as the same snapshot.

Storage includes:

- `club.calendar_import_state`: exact manifest bytes, digests, environment,
  source/export metadata, time zone, counts and readiness.
- `club.calendar_items`, `calendar_item_scopes`, `calendar_item_guests`:
  typed schedule, audience projections and same-organization references.
- `club.calendar_responses`: distinct source collection/kind/event/player keys,
  imported provenance and later RSVP actor/time.
- `migration.calendar_source_documents`, `calendar_import_issues`: each exact
  raw source object, disposition and fixed issue codes, owner-only.

State, typed rows, all source objects, quarantine and one safe import audit
commit together. Any quarantined row makes `is_complete=false`; all calendar
operations then return 503. Only a completed clean import can return a verified
empty `200` calendar. The CLI reports readiness only for a clean applied or
verified replayed snapshot, never for an offline or review-only result.

Exact-manifest replay is a no-op only while all retained source, manifest bytes,
typed fields, scopes, guests, responses, issues and import audit still reconcile.
It refuses different initial snapshots and detects later RSVP or canonical
changes; it does not overwrite those changes to recreate an old import. Raw
source is compared exactly, while jsonb projections use semantic normalized
values. This initial-only importer is not incremental synchronization. A
quarantined import requires an explicit reviewed recovery; there is no runtime
switch to mark it complete or replace its source.

## Runtime permissions and schema recovery

Migration `20261031170659_ScopedCalendar` adds these tables. Apply it as the
schema owner, then reapply `ops/postgres/identity-app-grants.sql` and run
`identity-app-permissions-check.sql` as the restricted runtime before starting
the updated API. The API does not migrate on normal startup.

The runtime reads only the import readiness/time-zone fields, curated calendar
tables and required team/group identity columns. Manifest bytes, source,
quarantine, full owner state and canonical writes remain denied. RSVP INSERT
permits the required keys, status, actor and time; UPDATE cannot change response
keys or source provenance. Restrictive RLS policies
confine mutations to `availability`/`event_attendance` and valid RSVP statuses;
physical attendance cannot be inserted/updated/deleted by this API role. Audit
remains INSERT-only. Application SQL still enforces per-request ownership; these
table policies do not independently implement every user's scope.

An entirely empty calendar extension can roll back and reapply. As soon as any
of its seven tables has data, its first `Down` operation raises
`calendar_data_requires_forward_recovery` before dropping tables. This includes
an imported complete-empty manifest and its state. Preserve protected source
manifests, checksums and a verified backup. Do not delete state, quarantine,
RSVPs or source objects to bypass the guard. Populated recovery needs a reviewed
forward migration or approved protected restore retaining subsequent writes.

Guard tests exercise the recorded EF Down operations in isolated transactions;
the view recovery probe executes its recorded Down/Up operations and reapplies
its grants. Neither globally downgrades later unrelated modules or their history.
This does not make an operator's deep EF downgrade atomic across migrations:
later empty/view-only migrations can complete their own `Down` before an earlier
populated-data guard refuses. Inspect actual migration history and restore any
already-removed extension with its runtime grants after such a refusal.

## Verification status and deployment limits

The source includes pure manifest/date/publication checks and guarded real
HTTP/PostgreSQL scenarios for import atomicity/replay, quarantine/readiness,
current role/scope revocation, guest privacy, paging, RSVP audit atomicity,
unchanged physical attendance and restricted runtime permissions. Frontend
checks cover generated API calls, child/organization isolation and stale-response
handling.

Observed on 2026-10-31: the complete 42-group HTTP/PostgreSQL run, including pure
calendar checks and the calendar end-to-end scenario, passed; all 26 policy
checks passed. EF reported no pending model changes. Runtime grants/probes,
controlled contract export/security assertions/client generation, actual empty
calendar Down/Up, populated rollback refusal, and both default/calendar preview
API smokes passed. Final frontend tests passed 90/90, with 20/20 focused calendar
cases, zero Svelte/type diagnostics, a successful production build and the final
production-only web package smoke. The
guarded browser preview verified actual parent login, frozen publication time,
confirmed Maybe RSVP, child isolation and retained response after switching
back, at 390 CSS pixels without horizontal page overflow. Graceful shutdown
removed the exact fictional calendar/identity/admin rows and retained audits.
GitHub checks were still pending at this checkpoint.
See the [dated verification record](verification-2026-10-31.md) for those limits
and subsequent observed outcomes.

No production records have been imported by this development slice. Staging
must use a separate Hostinger project/environment, database, secrets and preview
access; `lefteriafc.cy` and live production remain unchanged. Real source backup,
consistent export reconciliation, protected restore rehearsal, provider setup,
cached-PWA behavior and the remaining API/module ports are deployment gates.
