# Phase 2: Parity map, design specs, live content mirror and visual baseline

Version: phase-2

Goal: build the complete, evidence-based checklist that drives phases 3–5. It covers every original API operation, every original screen and the exact design specs, plus a preview with the same public content as lefteriafc.cy and side-by-side screenshots. This phase changes NO product behaviour under `backend/src` or `frontend/src`. Context and decisions are in [../overview.md](../overview.md).

Original source (read-only): `/app/.runtime/legacy-source` (ignored by git; uploaded zip of `MikeCy-com/Lefteria-FC` main). It has 65 files in `backend/routes`, 208 route decorators in `backend/server.py` and 553 in `backend/routes`, 70 admin files, 49 mobile pages and 33 public pages.

Existing partial audits to extend, not duplicate: `docs/implementation/{original-api-ui-parity-audit-2026-11-10,route-parity-inventory,go-live-gap-audit-2026-11-09,admin-original-parity-roadmap,original-ui-parity-plan,public-website-parity}.md`, `frontend/scripts/audit-original-parity.mjs`, `ops/release/go-live-gates.json`.

## Functional requirements

### API parity matrix
- REQ-31 [always] The system shall list every original HTTP operation (method + mounted path, including router prefixes) from `backend/server.py` and `backend/routes/*.py` in `docs/parity/api-matrix.json`. (interpreted)
- REQ-32 [always] The system shall record for each original operation its auth type, required role or permission, org/team/group scope rule, request fields, response shape and side effects. (interpreted)
- REQ-33 [always] The system shall give each original operation exactly one OS status from `ported`, `partial`, `missing`, `deprecated-by-design` or `provider-gated`, with the matching OS endpoint where one exists. (interpreted)
- REQ-34 [always] The system shall assign each `missing` or `partial` operation to one target phase (3 public, 4 admin, 5 mobile) and one module group. (interpreted)
- REQ-35 [always] The system shall produce a human-readable summary in `docs/parity/api-matrix.md` with counts per status, per module and per phase.

### UI parity matrix
- REQ-36 [always] The system shall list every original public route, admin module or tab, mobile page and role dashboard in `docs/parity/ui-matrix.json`, each mapped to its OS route or component and a status. (interpreted)
- REQ-37 [always] The system shall record for each original screen the API operations it calls, so the UI and API matrices cross-link. (interpreted)
- REQ-38 [always] The system shall record for each admin module and mobile page the permission or role that shows or hides it in the original. (interpreted)

### Design specs
- REQ-39 [always] The system shall extract exact design tokens per surface (public, admin, mobile, POS) from the original CSS, Tailwind config and inline styles into `docs/parity/design-tokens.md`: font families, weights, sizes, line heights, letter spacing, colours, container and page max-widths, rail and header sizes, spacing, radii and breakpoints.
- REQ-40 [always] The system shall list every difference between the original tokens and the current OS CSS in `docs/parity/design-diff.md`.

### Live public content mirror (preview only)
- REQ-41 [event] When the mirror script runs, the system shall fetch public lefteriafc.cy pages, public API responses and public images using GET only, at no more than 2 requests per second. (interpreted)
- REQ-42 [always] The system shall store raw mirror snapshots and media under `/app/.runtime/live-mirror/` only.
- REQ-43 [event] When the mirror is imported, the system shall load the public content (news, teams and groups, players, staff, fixtures and results, sponsors and partners, events, club settings and contact) into the preview database through the repo's existing public-content import path or a tool-layer adapter. (interpreted)
- REQ-44 [unwanted] If a mirrored item has no OS table to hold it yet, then the system shall record it as a gap in the matrices instead of inventing storage in `backend/src`.
- REQ-45 [unwanted] If a live request fails or needs login, then the system shall skip it and log it, without retrying in a loop.
- REQ-46 [always] The system shall keep the demo fixtures, test logins and the open owner setup intact after the import.

### Visual baseline
- REQ-47 [always] The system shall capture real browser screenshots of every public route on live lefteriafc.cy and on the OS preview at 390, 768, 1440 and 1920 px widths, with the same route and content. (interpreted)
- REQ-48 [always] The system shall store the screenshots under `/app/.runtime/visual-baseline/` with an index file, and add a summary table of visible differences per route to `docs/parity/visual-baseline.md`.

### Plan output
- REQ-49 [always] The system shall propose, in `docs/parity/phase-plan.md`, the split of phases 3, 4 and 5 into buildable sub-phases. Each sub-phase lists its API operations and screens, ordered by dependency. (interpreted)

## Quality requirements

```
Q-9  Tag: Coverage.API
     Scale: % of original route decorators (server.py + routes/*.py) present in api-matrix.json
     Meter: script that recounts decorators and diffs them against the matrix
     Must: 100
     Plan: 100
```
```
Q-10 Tag: Coverage.UI
     Scale: % of original public routes (App.js), admin modules (pages/admin/*.jsx) and mobile pages (mobile/pages/*.jsx) present in ui-matrix.json
     Meter: script recount
     Must: 100
     Plan: 100
```
```
Q-11 Tag: Accuracy.Status
     Scale: count of operations marked "ported" whose OS endpoint does not exist in contracts/openapi.json
     Meter: script check
     Must: 0
     Plan: 0
```
```
Q-12 Tag: Safety.LiveSite
     Scale: count of non-GET requests sent to lefteriafc.cy
     Meter: mirror script log review
     Must: 0
     Plan: 0
```
```
Q-13 Tag: Privacy.Git
     Scale: count of mirrored live records, images or screenshots in git-tracked files
     Meter: git status and git grep review
     Must: 0
     Plan: 0
```
```
Q-14 Tag: Fidelity.Mirror
     Scale: % of live public news articles and players visible on the OS public site after import
     Meter: count on live list pages vs. OS list pages
     Must: 90
     Plan: 100
```
