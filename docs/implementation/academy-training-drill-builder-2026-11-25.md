# Academy training exercise builder — 25 November 2026

Dependency: central Academy catalogue draft PR #476, head c0c0bd8ff31c03a90ae162b4cd94e660d5081ff8. This is a stacked development branch, not a release. The earlier exact-review PR #475 must be reconciled before merging the shared planner UI.

## Implemented slice

- Within the existing group training workspace, open a saved draft's exercise builder. Search the one central approved catalogue across every city and location; filter by category and age; add, remove and reorder exercises; allocate minutes within the existing session duration.
- The server validates exact current plan version and group; limits selections to 24 unique drills; allows 1–120 minutes per drill and rejects sequences exceeding the planned session duration. New selections require the exact approved catalogue revision. Existing frozen selections remain usable when the central record is changed or archived.
- Each save increments the existing plan version in the same database transaction, preserves immutable event history and invalidates the previous approval. Schedule changes retain the frozen exercises and cannot reduce duration below the sequence total. Published plans remain immutable.
- New GET and PUT /api/admin/academy/training/plans/{planId}/drills operations require WebAdmin, current Academy manager/Super Admin authorization and exact active group/city/location authority. No coach write privilege is introduced yet. Central catalogue access is not permission to inspect or modify another group's plans. No raw table grant or mobile endpoint is added.
- Legacy plan lists expose only an exercise count after verifying territorial scope; otherwise the new field is null. The new editor is unavailable without verified assignment.
- Explicit fail-closed rollout gate: the existing schedule-only approval cannot approve a plan containing exercises. Exercise-bearing plans remain private until complete-plan approval and safe member publication are implemented; this is not a completed training publishing workflow. Empty verified plans retain the existing approval path.
- OpenAPI and client type entries are manually staged, requiring regeneration and exact comparison using the compiled host.

## Tracker

| ID | Priority | State | Acceptance / evidence |
| --- | --- | --- | --- |
| DRILL-ATTACH-01 | P1 | Coded; focused checks passed | Exact plan/version, central approved catalogue selections, ordered frozen snapshots, atomic version increment; migration 0cb34be, API 085fe28, typed body 0f7dd55. |
| DRILL-DURATION-02 | P1 | Coded; DB not run | Reject overrun, duplicate IDs and duration shrink; preserve sequence history through ordinary plan changes. |
| DRILL-UI-03 | P1 | Coded; browser/build not verified | Greek picker, filters, paging, reordering, time budget, strict server response and no local persistence; ae609e8, 8962abf. |
| DRILL-AUTH-04 | P0 | Coded; integration not run | New drill read/write and attached-plan mutation check exact active territorial grants; no cross-city editing. 09a1b33 suppresses new count metadata outside authority. Existing legacy schedule-only routes still need a full territorial review. |
| DRILL-REVIEW-05 | P0 | Blocked pending next slice | Dedicated complete-plan approval and authorized frozen member projection. Old schedule-only approval explicitly blocked for plans with exercises. |
| DRILL-COACH-06 | P1 | Pending | Assigned coaches need exact group-specific draft authoring with separately authorized manager approval; central read remains shared. |
| DRILL-MEDIA-07 | P1 | Pending | Reviewed diagrams, protected media, session templates and structured exercise blocks. |

Verification: four focused test bodies from 7f56878 passed in an isolated JavaScript harness against reread committed source; TypeScript annotations stripped and Svelte compiler calls omitted. Two validator tests and two source assertion groups; no live authorization or database test executed. The initial harness failed because its type-stripping regex removed a function; corrected harness passed all four. This was a harness error, not a demonstrated product defect. Added rollback-only PostgreSQL sequence and two-city scope probe (b9b357e), wired into integration and CI scripts (4c55579, 5d36a11); those probes remain unexecuted. Container Node 22.16.0; repository requires Node 24; dotnet and psql unavailable. Full build, generated contracts, runtime DB grants, browser/role flows, CI and staging remain outstanding. No staging or production changes. Keep stacked draft unmerged; reconcile PRs #475 and #476 and run all mandatory checks before staging. 
