# Phase 2 journeys: parity map and baseline

Phase 1 journeys JRN-1..JRN-22 stay valid.

- **JRN-23**: open `docs/parity/api-matrix.md`.
  - expected: totals per status (`ported` / `partial` / `missing` / `deprecated-by-design` / `provider-gated`) that add up to the recounted number of original route decorators, plus a per-module table (e.g. charges, pos, trials, polls, social_network, family, notification_center).
- **JRN-24**: pick 5 random entries from `docs/parity/api-matrix.json` marked `ported`.
  - expected: each names an OS method and path that exists in `contracts/openapi.json`.
- **JRN-25**: pick 5 random entries marked `missing`.
  - expected: each has a target phase (3, 4 or 5), a module group, and the original role/permission and request fields filled in.
- **JRN-26**: open `docs/parity/ui-matrix.json` and look up the admin modules `ChargesTab`, `TeamsTab`, `EnhancedAcademyTab`, `PosTab`, the mobile pages `ParentDashboard`, `CoachDashboard`, `PlayerDashboard`, `FanDashboard`, `ManagementDashboard`, and the public routes `/`, `/news/:newsId`, `/player/:playerId`, `/vote`.
  - expected: each is present with its OS route or component (or `missing`), the API operations it calls, and the permission or role that shows it.
- **JRN-27**: open `docs/parity/design-tokens.md`.
  - expected: per surface, the exact font families, the font size scale, page and container max-widths, rail width (116 px) and header height (76 px), colours (`#080808`, `#f5a623`) and breakpoints, each citing its source file.
- **JRN-28**: open `docs/parity/design-diff.md`.
  - expected: a list of each token that differs between original and OS, with both values. An empty list is allowed only if the screenshots also show no differences.
- **JRN-29**: open the OS preview `/news`.
  - expected: the same news articles (titles, dates, images) as `https://lefteriafc.cy/news`, at least 90%. Opening one article shows its full text and image.
- **JRN-30**: open the OS preview `/team`, then a player page.
  - expected: the same first-team players as live lefteriafc.cy, with names and photos.
- **JRN-31**: run `git status` and `git grep` for a mirrored news title.
  - expected: no mirrored record, image or screenshot is tracked or staged.
- **JRN-32**: sign in at `/app/login` with the parent demo phone and code 123456 after the import.
  - expected: still works with the 3 children. `/admin/login` still offers owner setup.
- **JRN-33**: open `docs/parity/visual-baseline.md`.
  - expected: a row per public route with live and OS screenshot paths at 390, 768, 1440 and 1920 px and a list of the visible differences.
- **JRN-34**: open `docs/parity/phase-plan.md`.
  - expected: phases 3 (public), 4 (admin) and 5 (mobile) split into ordered sub-phases. Each lists its operations and screens, and together they cover every `missing` and `partial` matrix entry.
