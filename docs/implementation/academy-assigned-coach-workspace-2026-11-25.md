# Assigned Academy coach workspace — 25 November 2026

Stacked on draft #479 (cecef8cf34a86cb093aff5d18a778e7338aab191), which depends on #478, #477 and #476. Reconcile #475 overlapping planner changes before any merge. No staging or production mutation.

## Implementation

- Academy coach navigation "Τα τμήματά μου" uses the existing schedule screen with role-specific capabilities and a new private assigned-group directory. Directory only enumerates the canonical admin staff link, active coach staff record, explicit group memberships (including designated group coach), and verified active city/location mappings. Locks identity/staff link/membership and current scope rows before authorized reads/writes.
- Reuses private versioned schedule/drill planner, collision and published-plan immutability guards. Draft authoring uses assigned training permission only; coach may create/edit future or current-day private drafts in assigned groups, cannot archive or change the currently approved version. Manager can amend a draft and invalidate previous approval. Read-only approved drill sequence remains accessible for assigned coaches.
- New immutable submission table keyed by plan/version with actor and timestamp, exact coach confirmation and audit event. Editing creates a new version, which does not inherit an old submission. Manager list shows current submitted version; approval is still an explicit independent manager action (manager may also approve an unsubmitted manager-created or shared draft). Submission is not approval, notification or publication.
- Approval, full exercise review, recipient preview, publish, cancellation and central catalogue management stay outside coach permissions. Existing API paths only opt in to assigned coach authority for group-scoped draft/list/status operations; new submission and directory SQL routes are coach-only. Attendance session list, roster and versioned marks now allow assigned coaches, with all host group checks and guest/eligible child rules from previous migration; an unrelated group or revoked link/membership fails closed.
- Greek navigation and responsive existing components reused; no separate global organization picker, no new member payload, no live notification. Source/API contract and edge allowlists updated; generated API snapshots manually staged, must regenerate against compiled backend.

## Tracker

| Task | Priority | State | Acceptance/evidence |
| --- | --- | --- | --- |
| COACH-WORKSPACE-12 | P0 | Coded, focused checks passed | 3a4987d (migration), a1f1c16 (API), 0131035 (schedule), fb7d404 + 1045e31 (navigation); exact active assigned groups, not org-wide list. |
| COACH-DRAFT-14 | P0 | Coded, focused checks passed | Existing draft and drill authoring reuses canonical permissions, no archive/approved edit, exact revision submission. f9662a6, 7652fdd. |
| COACH-ATTENDANCE-15 | P0 | Coded, SQL/runtime unverified | eb7def3, f2e9f28, 2a983c6. All host group scope, outsider denial, versioned mark, revoked assignment denial. |
| COACH-VERIFY-16 | P0 | Partial | 7ecdb8e, 2a983c6 extend existing rollback-only SQL scripts; dc85d69 adds response, SQL/API source, UI navigation, fixtures, Svelte compilation tests. Five focused bodies passed; Svelte compile body omitted. |
| COACH-QUEUE-17 | P1 | Pending | Dedicated manager review inbox/return-for-revision with feedback and optional reviewed notifications. Current shared plan list marks submitted versions. |
| COACH-PLAYER-18 | P1 | Pending | Assigned-player development, match day, evaluation and private documents per explicit permissions. |
| RELEASE-13 | P0 | Blocked | Run full .NET/Node24/Svelte/PostgreSQL suites, grants and generated contracts; verify both roles on isolated staging including browser, revoked sessions, group transfers, concurrency, imported mappings, mobile/calendar and regression checks. |

Five focused test bodies ran against reread committed sources in an isolated JS harness (one extracted TypeScript response validation function with annotations removed; four source assertions). All passed; compiler-specific sixth test and PostgreSQL tests were not executed. No claim of functioning deployed UI or runtime access. Full CI not manually rerun per existing billing constraint. No merge or deployment. Known rollout decisions: coaches may edit unapproved submissions (new version needs new submission), cannot archive, cannot approve/publish/cancel; active verified site mapping required; first-team coaching workspace remains separate and is not implemented here. Approval queue/feedback/notifications pending. Security migrations retain forward-recovery-only policy.
