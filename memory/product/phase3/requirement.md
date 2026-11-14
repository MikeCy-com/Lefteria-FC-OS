# Phase 3A: Public design system and shell, plus club, squad and media data

Version: phase-3a (covers plan sub-phases 3.1 and 3.2, plus public image serving pulled forward from 3.3)

Goal: every public page looks exactly like live lefteriafc.cy at 390, 768, 1440 and 1920 px, with the same widths, fonts, font sizes, spacing, shell, header, sidebar, footer and cookie banner. Club, squad, academy, staff, seasons, venues and standings data is complete from the API, and the mirrored photos show. Context: [../overview.md](../overview.md). Evidence base: `docs/parity/{design-tokens,design-diff,visual-baseline,phase-plan,ui-matrix,api-matrix}.*`.

API parity rule (orchestrator decision): parity means the same capability and data. Each original operation needs an OS operation that serves the same data with the same rules. It does not have to use the same path. New operations follow the OS contract style (`/api/public/...` etc.) and go into `contracts/openapi.json`. A matrix row flips to `ported` only with the OS endpoint plus test evidence. Copying legacy paths is not required.

## Functional requirements

### Design system and shell (3.1)
- REQ-50 [always] The public site shall use the original font stacks: Manrope for body text, Bebas Neue where the original uses it, and the original condensed display font for headings and nav. Font files are self-hosted, licensed and served by the OS.
- REQ-51 [always] The public site shall use the original font sizes, line heights, weights and letter spacing per element (h1 58 px desktop / 38 px mobile, nav 11 px with icon and label side by side, body 16 px, etc.), as listed in `docs/parity/design-tokens.md`.
- REQ-52 [always] The public site shall use the original page and container max-widths, header height, rail width, spacing, radii, colours and breakpoints for every public route.
- REQ-53 [always] The public header, sidebar or rail, mobile menu, footer and header button (tickets, as on live) shall match the original structure and behaviour.
- REQ-54 [always] The public site shall show the original's cookie-consent banner with the same text and choices. The choice is stored client-side.
- REQ-55 [always] Elements that the OS adds but live does not have (for example the next-match countdown) shall be removed from public pages. (interpreted)
- REQ-56 [always] The public site shall serve `/checkout` and `/checkout/success` with the original layout. Card payment stays OFF and shows the original's unavailable or disabled state (fully built in 3.5).
- REQ-57 [always] Each of the 39 public screens in `ui-matrix.json` shall match the live screenshot at all 4 widths: page height within 10% and the same section order, given the same mirrored content. (interpreted)

### Club, squad, academy and season data (3.2)
- REQ-58 [always] The public API shall return every published player, including academy and unassigned players. A player page shall exist for every player shown on live.
- REQ-59 [always] The public API shall return academy groups with their players, fixtures, training sessions and stats, and `/academy/groups` and `/academy/:groupId` shall render them.
- REQ-60 [always] The public API shall provide the club profile, current season, seasons and archives, standings (with the original's configurable columns), venues, staff and staff detail, transfers, player and staff birthdays, teams, academy stats and drills, for every public screen that uses them.
- REQ-61 [always] Every new operation shall have a versioned migration where needed, the repo's public-read grant model, OpenAPI contract entries, generated TS types, and tests.

### Public image storage and delivery
- REQ-62 [always] The OS shall store public images (news, players, staff, partners, sponsors, groups, products) in the repo's media storage design (local volume or storage abstraction, matching the repo's deploy model) and serve them with cache headers.
- REQ-63 [event] When the live mirror is imported, the system shall attach the mirrored images, so cards and pages show the real photos instead of the club placeholder.
- REQ-64 [unwanted] If an image is missing, then the system shall show the original's placeholder behaviour.

### Dynamic content and permission basics
- REQ-65 [always] The public navigation, sections and module visibility shall come from backend data (published sections, module flags), not hard-coded lists, wherever the original reads them from the backend. (interpreted)

### Safety
- REQ-66 [always] Mirrored content and images shall stay out of git. Demo logins and the open owner setup shall keep working.
- REQ-67 [always] The `docs/parity/*` matrices and the visual baseline shall be rerun and updated. Every row flipped to `ported` shall carry its evidence.

## Quality requirements

```
Q-15 Tag: Fidelity.Typography
     Scale: % of visual-baseline captures where h1, body and nav computed font family + size equal live
     Meter: scripts/workspace/visual_baseline.py rerun, 39 routes x 4 widths
     Must: 95
     Plan: 100
```
```
Q-16 Tag: Fidelity.Layout
     Scale: % of captures with page height within 10% of live
     Meter: visual baseline rerun
     Must: 85
     Plan: 95
```
```
Q-17 Tag: Fidelity.Routes
     Scale: count of public routes that 404 on OS but 200 on live
     Meter: visual baseline rerun
     Must: 0
     Plan: 0
```
```
Q-18 Tag: Fidelity.Data
     Scale: % of live players / academy groups / partners shown on OS
     Meter: live-mirror counts
     Must: 100 / 100 / 100 (or a documented publication-rule reason)
     Plan: 100
```
```
Q-19 Tag: Performance.PublicPage
     Scale: ms median to load the public homepage HTML plus fonts
     Meter: tester browser, 3 loads
     Must: 4000
     Plan: 2000
```
```
Q-20 Tag: Quality.RepoChecks
     Scale: failing repo checks (dotnet build, PolicyChecks, npm test, npm run check, integration suite)
     Meter: builder run
     Must: 0
     Plan: 0
```
