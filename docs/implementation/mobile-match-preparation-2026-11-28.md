# Mobile match preparation — 28 November 2026

Baseline OS main: 9ad137b8a7119a7a5a171562584225e06ba412d9. Notification PR #474 is merged with six successful CI checks at head 9d4c828431e28b2d329df82b03a93c7a9c48ad23. Central drill library, coach autonomy and drill variants are already present in main; this slice avoids duplicating those changes.

## Original parity and acceptance

Original match roster and match submission routes were inspected read-only. OS has a scoped calendar RSVP roster endpoint, group match drafts and published fixtures but does not yet expose the full original fixture roster/stat submission workflow. Do not equate RSVP, squad selection, recorded attendance or published statistics.

| ID | Priority | Status | Acceptance / evidence |
|---|---|---|---|
| MATCH-UX-01 | P1 | Coded; focused tests passed | Coach/management calendar match displays authorized availability with 4 status totals, search, filters, partial list label, refresh and load more. Distinct from confirmed squad. Commits b8f4c35, c67f7c0, 5d5e894, 561b975. |
| MATCH-SEC-02 | P0 | Coded; source assertions passed | Protected roster responses use private/no-store; frontend rejects excessive, duplicate, malformed and noncanonical-offset pages and clears unverified results. On match pagination 503 retained controller data is not displayed as fresh; retry requires revalidation. Commits c6eb55c, 19cc2ea. |
| MATCH-API-03 | P1 | Outstanding | Persist a fixture-specific squad with current assignment and authorized guest scope, concurrency controls, explicit selections, role checks and read/write integration tests. |
| MATCH-API-04 | P1 | Outstanding | Coach stat submission, manager review, correction/rejection, and publication workflow; verify fixture/roster linkage, no public child data, audit and role restrictions. |
| MATCH-VERIFY-05 | P0 | Not verified | Run complete SvelteKit, ASP.NET, PostgreSQL, OpenAPI and browser CI, then verify actual role-specific staging workflows. |

Focused test commit 1cc81d2: 3 functional test cases and 1 source assertion group executed against committed source with TypeScript annotations removed, Svelte compilation omitted. All four passed. Full tests and CI remain pending at time of writing. No new endpoint or storage: existing calendar RSVP endpoint is reused. No staging/production change.
