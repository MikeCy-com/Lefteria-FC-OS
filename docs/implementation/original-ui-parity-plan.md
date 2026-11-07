# Lefteria FC original → OS visual and functional parity

**Status:** in progress, not pixel-perfect. The original project is read-only. Implement and verify in `MikeCy-com/Lefteria-FC-OS` only. Do not deploy or redirect `lefteriafc.cy` without explicit approval.

Reference baseline inspected **2026-11-07**:
- Original `MikeCy-com/Lefteria-FC`, `main` tree `91eafcc9efd01ad177b4f13a869cd5af7041e5b2` (React/JS).
- Rewrite `MikeCy-com/Lefteria-FC-OS`, `main` tree `5da09cd92f0fa333c356065f3922e67ca8c82d29` before UI parity PRs (SvelteKit/TypeScript, .NET 10).
- **Preserve source behavior and design**; do not copy runtime secrets, user data, or unsafe legacy auth patterns.

## Exact original brand baseline

| Property | Source reference | Original value |
| --- | --- | --- |
| Public body | `frontend/src/club-site/public.css` | Fira Sans, 400–700 |
| Headings | same | Fira Sans Extra Condensed, 500–900 |
| Public surface | same | `#080808` |
| Gold accent | same | `#f5a623` |
| Public rail, desktop | same | 116 CSS px |
| Public header, desktop | same | 76 CSS px |
| Mobile body | `frontend/src/mobile/mobile-theme.css` | Fira Sans |
| Mobile font sizes | `frontend/src/mobile/mobile-typography.css` | 26 display / 16 title / 14 card / 13 body / 12 label / 10 caption |
| Mobile black/gold | `frontend/src/mobile/mobile-theme.css` | `#080808` / `#f5a623` |

The OS already contains source-like public styling, but its role components and behavior are **not** complete ports. Do not claim any percentage of improvement without measured before/after results. “4K” means a 3840×2160 test viewport and assets whose *actual pixel dimensions* support the rendered display size; CSS sharpness cannot fabricate missing photo detail. Prefer the club's verified original artwork, not invented crests, people or fonts.

## Functional route parity / public menu

Source: `frontend/src/club-site/navigation.js` and `frontend/src/club-site/PublicNavigation.jsx`. Maintain all nine categories, all submenu destinations, dynamic Academy group routes, explicit active tab/query state, keyboard close/focus restoration, and responsive rail/dialog. The OS navigation inventory lives at `frontend/src/lib/public/navigation.ts`.

| Path | Original workflow | Required OS milestone |
| --- | --- | --- |
| `/join` | Academy trial/enquiry form, partner attribution | Restore enquiry backend contract, anti-abuse, persistence and confirmation; **not yet implemented** |
| `/trials` | First-team trial availability and submission | Port server eligibility, validated submission and admin review; **not yet implemented** |
| `/academy/registration` | Five-step child/parent/medical/consent/payment | Separate restricted API, consent records, legal review and secure retention; **not yet implemented** |
| `/vote` | Authenticated player-of-month votes/results | Auth/eligibility/audit/revote rules and real result publication; **not yet implemented** |
| `/cart`, `/profile`, `/register` | Legacy customer commerce and account | Separate protected account, cart and checkout workflows; **not yet implemented** |
| `/team`, `/academy`, `/news`, `/events`, `/shop` | Public content | Existing OS read-only projections require browser/API parity verification |

During migration, keep advertised URLs navigable to *honest* source-branded fallback pages and real support/directory links, never fake form submission, payment, votes, medical records or a saved registration.

## Seven screenshot acceptance surfaces

Capture **real browser screenshots** (never generated mockups) of: parent home/family/calendar/academy education; player home/calendar/profile; coach home/calendar/roster; fan home/club feed/family calendar; management/admin mobile; public homepage and every top-level/submenu route; protected web admin workspace and club editor. Use synthetic test data only, with documented fixture provenance. Capture original and OS at the **same** viewport, pixel ratio, OS font availability, language, route and state; include empty/error/loading cases. Do not upload private child details or production credentials.

Viewports: **390×844**, **768×1024**, **1440×900**, **1920×1080** and **3840×2160**. For each: compare typography, line heights, colors, image crops, crest sizes, rail/header/bottom-nav geometry, content overflow, animation/responsive motion, button hit areas, focus and keyboard navigation. Do not enable CSS to hide visual differences under overlays or claim “pixel perfect” from code review alone.

## Delivery gates

1. Verify each source React/CSS page or behavior and map it to exact Svelte components, tests, public routes, and protected API operations.
2. Implement in small OS-only branches; expose no features not backed by real authorization and privacy checks.
3. Run frontend component/contract/smoke tests, real PostgreSQL integration and all **five GitHub CI checks**; merge to OS `main` only after green.
4. Render source and OS with same disposable fixture. Record browser screenshots and diff at the prescribed viewports, inspect actionable differences and iterate. Images sourced from 512px files are **not** genuine 4K content.
5. Only mark a route/role **visual and functional parity verified** after both navigation and all active flows succeed in the browser, including auth, input errors and feedback, not merely after a Svelte component renders.

## Implemented initial CSS/navigation slices

- PR #75: original-brand tokens, 4K public refinement, mobile typography and accessible modal navigation focus.
- PR #76: remove dead public destinations with source-branded honest route landings and safe aliases (transactions **not** ported).
- PR #77: original-brand admin 4K readability/contrast refinements (pending verification at authoring).

The seven-screen pixel baseline and full behavioral migration remain open. Do not promote these small steps as completion.
