# Requirements: Lefteria FC OS (workspace run)

## Core Value
Run the user's existing Lefteria FC OS repo (imported from GitHub `MikeCy-com/Lefteria-FC-OS`, branch `main`) in this Emergent workspace using its own technology: SvelteKit + TypeScript web, ASP.NET Core on .NET 10 API, PostgreSQL. Load the repo's own made-up demo data so the public site, the admin (`/admin`) and the mobile member app (`/app`) can be reviewed. After that the user gives the next instructions. Do not add features or change product behaviour in this run.

## Personas & Roles
- **Club owner (the user)**: creates the founding super-admin account with a one-time setup code, then uses `/admin`.
- **Mobile demo members**: made-up player/fan, parent, coach and management identities from the repo's fixtures. They sign in at `/app/login` with test phones and code `123456`.
- **Public visitor**: browses the Greek public website without signing in.
- Roles, permissions and FC/Academy scoping are already defined in the repo (see `/app/AGENTS.md`, `/app/docs/`). Do not change them.

## Features
| id | feature | persona | phase |
|---|---|---|---|
| F-1 | The workspace runs the repo's own stack: .NET SDK from `global.json`, PostgreSQL 18, Node 24. It recovers after a workspace restart. | all | 1 |
| F-2 | Fresh PostgreSQL database. All repo migrations applied. Restricted runtime roles set up with the repo's `ops/postgres` scripts. | all | 1 |
| F-3 | The .NET API is served at `/api` on the preview URL. The repo's committed contract is served at `/api/openapi.json`. | all | 1 |
| F-4 | The SvelteKit web is served at the preview root: public site, `/admin`, `/app`. | all | 1 |
| F-5 | Founding owner setup is available at `/admin/login`. The one-time setup code is given to the user, never committed. | club owner | 1 |
| F-6 | Made-up demo data from the repo's own fixtures: roster, staff, accounts, Oct-2026 calendar, club profiles. | all | 1 |
| F-7 | Mobile test logins: made-up phones plus code `123456`, using the repo's own preview test tooling. No bypass is added to product code. | mobile demo members | 1 |
| F-8 | Outside services stay OFF: Twilio SMS, Payabl, Web Push, OpenAI article drafts, legacy mobile bridge. | all | 1 |
| F-9 | The repo's own developer checks run and their results are reported. | club owner | 1 |
| F-10 | Full parity map from the original source (`/app/.runtime/legacy-source`, uploaded zip of `MikeCy-com/Lefteria-FC`). Every original API operation (about 761 route decorators) is mapped to its OS status. Every original screen is mapped too: 33 public pages, 70 admin modules, 49 mobile pages. | club owner | 2 |
| F-11 | Exact design specs per surface from the original CSS and Tailwind: page widths, fonts, font sizes, line heights, spacing, colours, breakpoints. | club owner | 2 |
| F-12 | Public content from live lefteriafc.cy (news, teams, players, fixtures, sponsors, photos) is copied with read-only GET requests into the preview database only. It is never committed to git. | club owner | 2 |
| F-13 | Side-by-side screenshot baseline of live and OS public pages at 390, 768, 1440 and 1920 px. | club owner | 2 |
| F-14 | Public website parity: every page, form and public API works and looks exactly like the original (widths, fonts, font sizes). | public visitor | 3 (may split) |
| F-15 | Admin panel parity: every original admin module and its APIs. | club owner, staff | 4 (split by module group) |
| F-16 | Mobile app parity: all 5 role dashboards and every original mobile page with its APIs. | members | 5 (split by role) |
| F-17 | Dynamic permissions: admin navigation, controls and mobile screens follow the current role, capabilities and organization grants sent by the backend. Changing a permission changes the UI without a code change. The server re-checks every operation. | all | 3–5 |
| F-18 | Features that need outside services are built fully but stay OFF (fail closed) until the user adds keys: SMS, card payments, push, social posting, backups, AI. | all | 3–5 |

## Phases
- **Phase 1: Run the solution here.** DONE. The existing app is live on the preview URL on its own stack, with demo data, test mobile logins and owner setup available. Delivered F-1 to F-9. Docs: [phase1/requirement.md](phase1/requirement.md), [phase1/journeys.md](phase1/journeys.md).
- **Phase 2: Parity map and baseline.** DONE: 785 original operations (28 ported, 30 partial, 687 missing, 38 provider-gated, 2 dropped) and 218 screens mapped. Docs in `docs/parity/`.
- **Phase 3A (plan 3.1 + 3.2 + public images):** public design system and shell, plus club, squad and academy data. Docs: [phase3/requirement.md](phase3/requirement.md), [phase3/journeys.md](phase3/journeys.md).
- **Phase 3B (plan 3.3 + 3.4):** news, media, fixtures, SEO/OG, engagement and forms. **Phase 3C (plan 3.5):** shop and checkout.
- API parity rule: the same capability and data under the OS contract style. Matching legacy paths is not required.
- **Phase 3: Public website parity.** Delivers F-14, plus F-17 and F-18 for the public site.
- **Phase 4: Admin panel parity.** Delivers F-15, plus F-17 and F-18 for admin. Split into module groups from the Phase 2 map.
- **Phase 5: Mobile app parity.** Delivers F-16, plus F-17 and F-18 for mobile. Split by role.
- The user's order of priority: API and UI first, in the order public, then admin, then mobile.

## Build rules (user confirmed: keep the repo's strict rules)
- Small audited slices. Each new private operation gets a versioned migration, server-side authorization with org/team/group scope, an explicit SQL grant and the repo's checks.
- Honest unavailable states; never fake success. Follow `/app/AGENTS.md`.
- The original source is READ-ONLY reference. Never write to lefteriafc.cy or its data. Only read-only GET of public pages and public APIs is allowed.

## UI/UX Rules
- No UI, copy or design changes. The app keeps its Greek content, fonts, role layouts and the exact **Under Developments** label on unported functions.
- Pages with no data show the repo's own empty or unavailable states. Never add placeholder or mock screens.

## Integrations
| service | decision | notes |
|---|---|---|
| Twilio SMS (login codes, payment SMS) | OFF | Mobile login uses the repo's preview test tooling: fixed code `123456`, accepted only for the generated made-up phones. |
| Payabl (sandbox payments) | OFF | `Payabl__Enabled=false` |
| Web Push / VAPID | OFF | `WEB_PUSH_ENABLED=false` |
| OpenAI (Hub AI education drafts) | OFF | `LEFTERIA_AI_ENABLED` unset or false. The code calls api.openai.com directly. The Emergent key is not used. |
| Legacy mobile bridge | OFF | `LEGACY_MOBILE_BRIDGE_ENABLED=false` |
| Emergent deploy | Not applicable | Preview only. The user goes live through their own Hostinger pipeline. |

## Edge Cases
- Workspace restart: tools installed outside `/app` may disappear. Startup must reinstall or repair them on its own. The database must survive.
- The workspace launchers are fixed: port 8001 runs `uvicorn server:app` in `/app/backend`, and port 3000 runs `yarn start` in `/app/frontend`. A small bridge is needed. Ingress sends `/api/*` to 8001 and everything else to 3000.
- Any existing admin record closes founding setup. Demo seeding must not close it before the user creates their owner account.
- Testers and builders must never complete founding setup. It is one-time and belongs to the user.
- Cookie-based admin login needs the HTTPS preview origin and correctly trusted forwarded headers.
- Preview signing/OTP keys may be random per run. A restart may sign mobile users out. That is acceptable.

## Seed Data
- Since phase 2, public website content mirrored read-only from lefteriafc.cy goes into the preview database only. Raw snapshots and media live under `/app/.runtime/` (ignored by git). They never go into fixtures, git or docs.
- Only the repo's existing made-up fixtures, such as those used by `backend/tools/LefteriaFc.MobilePreview` (calendar mode and web-platform club fixture). Do not invent new records.
- No production data and no real people. Demo phones are the fixtures' made-up numbers.
- Calendar fixture month is October 2026. December has the pagination scenario.
