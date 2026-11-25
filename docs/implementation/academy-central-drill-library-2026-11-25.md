# Central Academy drill library — 25 November 2026

Owner decision: Βιβλιοθήκη ασκήσεων is CENTRAL across every Academy city and location.
The catalogue is fixed to lefteria_fc_academy; it has no city, location or group selector or ownership column. No manual organization switching. This is not a change to the territorial authorization of players, schedules, matches, finance or training writes.

## Implementation and permissions

- Academy-granted super_admin, manager and coach may read approved drills, regardless of city/location. Current live admin session/account and organization grant are locked and rechecked in SQL. First-team-only accounts and unrelated roles are denied. Central read does not imply access to any roster or group.
- Conservative first slice: only Academy-granted Super Admin may create/edit/approve/archive/restore. This is an implementation choice; future staff suggestions may use a separately reviewed workflow. No implicit central editing grant for all coaches/managers.
- The shared catalogue has eight category and six age-band values, duration, objective, multiline instructions, stable ID and optimistic version. Edits and restore require fresh approval, and temporarily remove the drill from the approved catalogue. Old snapshots are retained. No hard deletion, imported data or demo content.
- GET /api/admin/academy/drills (search, category, age, status, offset); POST same path; PUT /api/admin/academy/drills/{drillId}. Authenticated WebAdmin routes only. Narrow execute grants, no runtime raw-table access, fixed search paths, explicit preview edge entries and private/no-store responses.
- Central Greek page /admin/drills, menu in Αγωνιστικό & Ακαδημία and direct navigation from Academy group training workspace. Super Admin form and version-specific lifecycle actions. No claim that mobile drill attachments or member content are available.
- OpenAPI snapshot and corresponding client types are hand-staged; regenerate using compiled backend and normal generator, compare exact output before merge.
- Disposable SQL lifecycle checks wired into existing local and CI test scripts. No manual workflow dispatch or deployment performed.

## Tracker

| ID | Priority | State | Acceptance / evidence |
| --- | --- | --- | --- |
| DRILL-CENTRAL-01 | P0 | Coded; focused JS/source checks passed | Central approved read across two cities, no territory columns, live grant check. Migration 0f25890, typed page correction 82d6008. |
| DRILL-CENTRAL-02 | P0 | Coded; DB not run | Versioned create/edit/approve/archive/restore and immutable audit snapshots; stale conflict. API b6f84ed, registration 9bea62b. |
| DRILL-CENTRAL-03 | P0 | Coded; role provisioning unverified | Explicit narrow admin EXECUTE grants 7c3a027, permissions allowlist c9f4469; no direct mobile/table/helper access. |
| DRILL-CENTRAL-04 | P1 | Coded; browser/build not verified | Greek catalogue, filters, pagination, editor, menu and group link; 5657ddc, d5b911c, 0b9855d, 18df632. |
| DRILL-CENTRAL-05 | P0 | Tests authored; partial harness pass | 544ad24 rollback-only PostgreSQL test: city A manager, city B location manager, coach share same approved row; draft/write denial, foreign role, revocation and six versions. Frontend 8e4a5a8 checks. |
| DRILL-ATTACH-01 | P1 | Pending | Attach ordered approved drill versions to existing exact group training plan, atomic snapshot, optimistic plan version and approval invalidation. Never broaden planner grants by central read. |
| DRILL-MEMBER-02 | P1 | Pending | Publish frozen member-safe exercises to eligible players/verified parents only via canonical training event. No private notes, draft or foreign child access. |
| DRILL-MEDIA-03 | P1 | Pending | Protected diagrams/media, reviewed storage, no arbitrary URL fetch. |
| DRILL-HISTORY-04 | P1 | Partial | Durable internal history stored; authorized UI/API history reader and audit display still needed. |
| DRILL-STAFF-05 | P1 | Pending | Coach-side assigned-group session editing and manager suggestion workflow; no global catalogue write entitlement. |
| COACH-MATCH-06 | P1 | Pending | Continue match reports, individual statistics and facility-conflict workflows. |

Focused verification: reread committed sources and ran the four new test bodies in an isolated JS harness with TypeScript annotations stripped and Svelte compiler calls omitted. All four passed (role and validator functional tests, SQL/API/UI source assertions). This is not database, compilation, browser, security penetration, full Node suite or staging evidence. PostgreSQL/dotnet not installed in current execution container; Node available is 22, repository requires 24. No CI rerun. PR must remain draft until compiled contract and client regeneration, full CI, disposable PostgreSQL grant/migration/lifecycle checks, authenticated multi-city browser flows, mobile viewport, and staging acceptance. No production or staging change. Previous PR #475 is independent; reconcile shared files with #472 and other open UI branches before merge.
