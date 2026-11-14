# Lefteria FC OS — workspace PRD

## Original problem
Run the existing Lefteria FC OS repository on its own stack (SvelteKit + TypeScript, ASP.NET Core .NET 10, PostgreSQL 18). Then reach full parity with the original lefteriafc.cy API and UI, in this order: public website, then admin, then mobile. Pages, widths, fonts and font sizes must match exactly. Admin-driven and role-driven UI must change dynamically from the backend.

## Architecture
- The fixed supervisor runs uvicorn on 8001, which loads `backend/server.py`. That file is an ASGI bridge that proxies `/api/*` to the .NET host on 127.0.0.1:5080.
- `yarn start` on 3000 runs `scripts/workspace/web-start.mjs`, which does `npm ci` and starts `vite dev`.
- PostgreSQL 18 listens on 127.0.0.1:54329 (db `lefteria_rewrite_identity_tests`). Runtime state is in `/app/.runtime` (git-ignored).
- Demo identities are listed in `memory/test_credentials.md`. Owner setup stays open for the user.

## Done
- 2026-11 Phase 1 (accepted): native runtime, bridge, bootstrap, demo data, developer checks.
- 2026-11 Phase 2 (parity analysis, no product changes):
  - `docs/parity/api-matrix.{json,md}`: 785 original operations. 28 ported, 30 partial, 687 missing, 38 provider-gated, 2 deprecated-by-design.
  - `docs/parity/ui-matrix.json`: 218 screens (47 public routes, 66 admin modules, 26 mobile pages, plus tabs and role dashboards).
  - `docs/parity/design-tokens.md` and `design-diff.md`: 518 public, 495 admin, 658 mobile and 164 POS token differences.
  - `docs/parity/live-mirror.md`: live public content imported into the preview through the repository's own importers. 11/11 news, 15/33 players.
  - `docs/parity/visual-baseline.md`: 256 Chromium captures (32 routes × 4 widths × live/OS). Screenshots are in `/app/.runtime/visual-baseline`.
  - `docs/parity/phase-plan.md`: 755 open operations and 218 screens assigned to sub-phases 3.1–3.5, 4.1–4.5 and 5.1–5.4.
  - Scripts: `scripts/parity/{build_matrices,recount,design_tokens,phase_plan}.py`, `scripts/workspace/{live_mirror.py,mirror_manifests.py,import-live-mirror.sh,visual_baseline.py}`.

## Backlog
- P0 Phase 3 public: 3.1 design system and shell, 3.2 club and squad data, 3.3 news, media and SEO, 3.4 engagement, 3.5 shop and checkout (provider OFF).
- P1 Phase 4 admin: 4.1 shell and dynamic permissions through 4.5 marketing and social.
- P2 Phase 5 mobile: 5.1 auth and family through 5.4 payments.
- Known gaps: no public media serving for imported images; players without a team and academy groups are missing from public sections; `/checkout` routes are missing; 6 admin grant scripts are unapplied.
