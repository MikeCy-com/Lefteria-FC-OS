# Academy complete training review and member publication — 25 November 2026

Stacked dependency: PR #477 (exercise builder, head 9806dc3e7852af892cb5af009c3bc48313b6f8b7) depends on central catalogue PR #476. No production or staging change. Earlier standalone PR #475 overlaps the planner UI; the key-based review and strict preview allowlist are now included in this stacked branch, but reconcile the PRs before merge.

## Implemented slice

- Manager or Super Admin opens the exact saved exercise plan revision in read-only complete review mode. Review includes ordered frozen drill contents, timings, objectives, instructions, schedule and private notes. Confirmation is consumed and reset when member summary changes.
- Separately authored, explicitly reviewed member-facing plain-text summary (1–1200 characters, no unsafe control characters except line breaks). No automatic copying of internal coaching instructions, player information, roster data or private notes into a member-visible projection. The reviewer must ensure the summary is appropriate for the audience.
- New authenticated POST /api/admin/academy/training/plans/{planId}/drills/approve. SQL locks live manager session, exact Academy grant and current active group/city/location authority, then locks the plan and requires its exact current unpublished draft version. The immutable approval contains full frozen drill snapshots and the member summary. The old schedule-only approval path remains blocked for nonempty drill plans.
- Existing recipient preview and publication functions recheck exact territorial authorization for exercise-bearing plans, require an exact complete approval and freeze only the reviewed member summary into the existing canonical event's summary field. The existing mobile calendar maps that field to member_summary and already renders it for eligible parents/players (and existing explicitly authorized family calendar delegations). No raw exercise snapshots or coaching notes in calendar projections. Member delivery uses existing canonical group membership predicates and current authorization; no new global mobile endpoint or table access.
- Publishing and cancelling remain separate confirmed actions. Exercise-bearing cancellation also checks exact current territory. Published sequence stays immutable; existing RSVP/attendance data retained. No automated SMS/push notifications introduced.
- Schedule-only confirmation in shared planner UI is now bound to exact plan ID and version; strict recipient preview response allowlist includes a member summary only for a plan containing drills. Fixed initial preview guard that otherwise rejected nonempty reviewed plans.
- API snapshot and client types hand-staged pending compiled regeneration. Existing raw SQL and integration test files are updated, not run.

## Tracker

| ID | Priority | State | Acceptance / evidence |
| --- | --- | --- | --- |
| DRILL-REVIEW-05 | P0 | Coded; focused checks passed | Exact complete-plan immutable human approval, safe summary, old bypass blocked. Migration d1db7e9, API d4e3185, review UI 402c3e4. |
| DRILL-PUBLISH-06 | P0 | Coded; DB/browser unverified | Exact approved version, group-authorized preview/publication/cancel, member summary only, existing calendar eligibility. 89bb7e2, 3b05af3, 6595c96. |
| DRILL-VERIFY-07 | P0 | Tests authored; focused checks passed | Rollback-only exercise lifecycle extended for foreign-site/stale/unconfirmed review, safe preview/publication, native membership and cancellation (75827f5, ac279ae). Frontend tests 3f13dc4. |
| DRILL-COACH-08 | P1 | Pending | Assigned coach authoring for exact current group only, no central editing or self-approval; manager approval separation. |
| TRAINING-TERRITORY-09 | P0 | Pending | Complete legacy schedule-only plan list/create/update/approval/preview/publication/cancellation territorial review. Existing organization-only legacy paths remain a known gap; new drill-specific paths and exercise-bearing publication/cancellation check territory. |
| DRILL-MEMBER-10 | P1 | Partial | Member summary through current calendar; dedicated structured exercise display, role-specific mobile acceptance tests and protected diagrams/media still pending. |
| DRILL-TEMPLATE-11 | P1 | Pending | Reusable sessions and diagrams, coach contributions and historical review UI. |

Evidence: reread the committed sources and ran four new focused test bodies in an isolated JavaScript harness; extracted TypeScript validation functions with type annotations removed and omitted Svelte compilation. All four passed (summary/approval response, strict preview, SQL/API source assertions, UI source assertions). This is not compiled code, live SQL execution, authorization penetration testing or browser evidence. The extended disposable PostgreSQL lifecycle is still unexecuted. Local container verified Node v22.16.0; repository requires Node 24; dotnet and psql not installed and no local repo checkout. Full Svelte/.NET build, generated contracts, DB grants and runtime checks, mobile parent/player/foreign-child tests, desktop/mobile browser flows, CI and isolated staging remain release blockers. Do not merge or deploy before mandatory checks. No manual CI rerun, no staging or production mutation.
