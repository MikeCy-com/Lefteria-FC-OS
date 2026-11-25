# Academy coaching workflow parity — 25 November 2026

Baselines: OS `ec138ef114fbfa4e754430b4439dfbb88c4c97ec`;
original Lefteria FC `91eafcc9efd01ad177b4f13a869cd5af7041e5b2`.
Original repository read-only. This branch is independent of mobile PR #474.

## Implementation tracker

| ID | Priority | Status | Acceptance / dependency |
| --- | --- | --- | --- |
| ACADEMY-REVIEW-01 | P0 | Coded; focused source checks passed | Only the reviewed draft ID and version enables approval; consume confirmation before POST, clear on scope/edit/reset, verify exact response. Commit 52005e9. |
| ACADEMY-REVIEW-02 | P0 | Coded; focused validator checks passed | Recipient preview accepts exactly 12 approved fields; arbitrary unknown keys rejected. Commit 911f701. |
| ACADEMY-DRILL-01 | P1 | Missing in OS parity inventory | Reusable coaching drill catalogue, bounded filters, read/write scoped role authorization, draft/archive/restore, optimistic version and immutable history. |
| ACADEMY-DRILL-02 | P1 | Missing | Protected diagrams/media with reviewed storage and safe retrieval; no arbitrary URL fetching. Depends on catalogue. |
| ACADEMY-DRILL-03 | P1 | Missing | Ordered drill attachments to exact native training plan revision; snapshot drills so library edits/archive cannot mutate past plans. |
| ACADEMY-DRILL-04 | P1 | Partial | Human approval and native calendar publication already exist. Add separately authorized staff draft and frozen member-safe projections of attached drills; reapproval required for changes. |
| ACADEMY-DRILL-05 | P1 | Not verified | Assigned coaches manage permitted groups only; parent/player read approved sessions only; fan and unrelated group/organization denied. Existing OS manager-only plan grants must not be broadened implicitly. |

## Original API inventory

Source: [original drills routes](https://github.com/MikeCy-com/Lefteria-FC/blob/91eafcc9efd01ad177b4f13a869cd5af7041e5b2/backend/routes/drills.py).
These are router-relative paths under the legacy API prefix; OS replacement routes need not reuse the legacy names.

- GET /drills, GET /drills/{drill_id}: member-safe library reads.
- GET /admin/drills, GET /admin/drills/{drill_id}: staff catalogue and detail.
- POST /admin/drills, PUT /admin/drills/{drill_id}: create and versioned editing.
- DELETE /admin/drills/{drill_id}, POST /admin/drills/{drill_id}/restore: archive/restore.
- GET /admin/drills/{drill_id}/versions: immutable history.
- POST /admin/drills/upload-image: diagram media.
- PUT /admin/sessions/{event_id}/drills, GET /sessions/{event_id}/drills, GET /admin/sessions/{event_id}/drills: attachments and separate audience views.

The original audit records additional incomplete work (controlled taxonomy, validated pitch diagrams, reviewed catalogue and publication). Do not import unsupported legacy functionality or claim a reviewed catalogue exists.

OS existing native training plans support manager-owned versioned drafts, approval, distribution preview, calendar publication and cancellation. The current HUB editorial drill content is not a coaching drill catalogue or session association. Preserve canonical calendar IDs, attendance, RSVP and historical import provenance.

## Verification

Regression tests added in 680e5ac. Re-read exact committed files and executed both new test bodies in a JavaScript harness: preview acceptance, six unknown-key rejections, missing-key rejection and source invariants all passed. TypeScript annotations removed and Svelte compiler calls omitted for this focused harness. Full Svelte compile/check/build, browser accessibility and actual authorization/database/CI/staging remain outstanding. No new backend routes, SQL grants, migrations or provider actions. No staging or production modification.

Release gates: exact-head mandatory CI, authorized synthetic manager review of A versus B (including different revisions), approval failure/retry and scope changes; preview extra-field rejection; no publication from approval; role denial. Keep draft until verified. Implement catalogue persistence and explicit scope policy next, then attach snapshots to approved plan revisions; do not introduce an independent duplicate training calendar.
