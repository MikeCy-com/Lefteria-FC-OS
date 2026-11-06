# Lefteria FC → Lefteria FC OS: source parity execution contract

Audited on **2026-11-06** using the connected private GitHub repositories. This file is an actionable baseline, **not a claim of completed migration**.

| Item | Pinned Git commit |
| --- | --- |
| Source (`MikeCy-com/Lefteria-FC`) | `91eafcc9efd01ad177b4f13a869cd5af7041e5b2` |
| Target (`MikeCy-com/Lefteria-FC-OS`) | `72988a91680ce47534b98aa6e77480eae2f899a6` |

GitHub recursive trees were **not truncated**: source **1,014** tracked files; OS **495**. These are **total-file counts, not missing-feature counts**. Source's `backend/server.py` is ~335 KB, `frontend/src/App.js` ~110 KB, with 65 `backend/routes/*.py` modules and 70 `frontend/src/pages/admin/*` files. The source uses FastAPI/React/MongoDB; OS keeps SvelteKit/TypeScript, ASP.NET Core/.NET 10, PostgreSQL, Docker and Caddy. Preserve behavior and appearance, **not** source implementation choices or vulnerabilities.

## Baseline and non-negotiables

- Do not modify, deploy, import into, or connect the live source application/production database as part of this migration. OS `main` and the existing isolated preview are also not automatic release targets.
- Do not mechanically copy the Python, React or MongoDB runtime into OS. Implement domain behavior behind strongly typed C# application services, authorization policies, PostgreSQL persistence, OpenAPI contracts and SvelteKit views.
- Reuse and extend OS's existing identity, role switching, parent/player/organization scope, imports, public sections, news, admin login and calendar. Do not create duplicate parallel implementations.
- Treat source **GitHub main** and **running production** as different evidence; this baseline does not establish deployed SHA, database schema or live data. Compare future source revisions explicitly and repeat relevant tests.
- Preserve user-facing Greek content, branded layouts, responsive behavior, route/deep-link compatibility, UI flows, user roles and output semantics where safe; upgrade security and accessibility rather than preserving defects.
- Source contracts are reference material, not unquestioned authority. No secret, token, real minor/player data, archived upload, or private contact record goes into tests, code, artifacts or logs.

## Cross-repository feature ownership map

| Source files / experience (representative) | OS destination / implementation | Target state and gate |
| --- | --- | --- |
| `frontend/src/App.js`, `pages/AcademyLandingPage.jsx`, public `club-site/*` | `frontend/src/routes/*`, `lib/public/*`, `lib/server/*`; `PublicContent` API | Existing public slice; prove exact route, metadata, layout, organization filters and browser parity |
| `frontend/src/mobile/MobileApp.jsx`, `mobile/pages/MobileLoginPage.jsx`, `backend/routes/mobile_auth.py` | `lib/mobile/*`, `Identity/*`, `MobileAuth/*` | Existing identity shell; real SMS/2FA and session security still gated |
| `mobile/pages/ParentDashboard.jsx`, `FamilySettingsPage.jsx`, `backend/routes/family.py` | New `Application/Family`, `Api/Family`, `Infrastructure/Family`, SvelteKit mobile parent views | Require verified legal guardian → player mapping; no cross-family data; explicit negative authorization tests |
| `mobile/pages/PlayerDashboard.jsx`, `PlayerDevelopmentPage.jsx`, `backend/routes/member_development.py`, `player_evaluations.py` | `Application/PlayerDevelopment`, `Api/PlayerDevelopment`, private scoped Svelte views | Age-aware privacy rules, player+coach scoped access, history and test fixtures |
| `mobile/pages/CoachDashboard.jsx`, `backend/routes/admin_calendar.py`, `match_rosters.py` | Extend existing `ClubCalendar`, `Roster`; add coaching application services/views | Team/group scope, match and training records, no implicit organization-wide access |
| `mobile/pages/ManagementDashboard.jsx`, `pages/AdminPanel.jsx`, `pages/admin/*` | Extend `WebAdmin`, distinct `Application/*` slices, `routes/admin/*` | Capability matrix and organization authorization on every read/write; negative tests |
| `backend/routes/charges.py`, `financial.py`, `stripe_checkout.py`, `expenses.py`, `pos.py` | New `Domain/Finance`, `Application/Finance`, `Infrastructure/Finance`, `Api/Finance` | Mandatory ownership checks, decimal-safe money, payment idempotency, sandbox-only provider tests, financial reconciliation |
| `pages/ShopPage.jsx`, `NewShopPage.jsx`, `CartPage.jsx`, `CheckoutPage.jsx` | `Application/Commerce`, `Api/Commerce`, SvelteKit shop and checkout | Basket/order consistency, webhook verification/replay, transactional inventory and user-access tests |
| `mobile/pages/ChatPage.jsx`, `backend/routes/social_communities.py`, `chat_moderation.py` | `Application/Communication`, `Api/Communication`, Svelte views | Moderation, minor protections, participant-only access, retention, throttling |
| `mobile/pages/ReelsPage.jsx`, `SocialNetworkPage.jsx`, `backend/routes/social_network.py`, `videos.py`, `social_media.py` | `Application/Social`, `Api/Social`, media adapter/storage, Svelte views | Strict upload sanitization and moderation, permissions, privacy, media limits, no unsafe public delivery |
| `backend/routes/notification_center.py`, `push_targeting.py`, `notification_schedule.py`, `backend/notification_delivery.py` | `Application/Notifications`, durable PostgreSQL outbox/worker, provider adapters | Explicit opt-in/preferences, idempotency/retries, role-scoped deep links; no live sends without approval |
| `backend/routes/players_crm.py`, `trials.py`, `player_documents.py`, `staff_compliance.py` | `Application/CRM`, `Api/CRM`, restricted document storage | Child privacy, signed access, retention, explicit record+organization permission |
| `backend/routes/analytics.py`, `quarterly_report.py`, `backup.py` | Operational reports and protected admin-only tooling | Restrict exports, redact data, test backups and restoration separately |
| `mobile/pages/EducationHubPage.jsx` | New mobile education module for players and guardians | Age-appropriate player education, parent guidance and reviewed club content |

Above are starting ownership assignments, **not a claim that each route is fully implemented**. The authoritative detailed route status is in [route-parity-inventory.md](route-parity-inventory.md); update it **per PR** with tested evidence. Source has additional routes (academy matches, loyalty, fan engagement, polls, referrals, marketing, sponsors, resources, videos, audit, security operations, etc.); no route should disappear silently.

## Security, resilience and production hardening gates

1. **Authorization:** enumerate every original and new API endpoint; classify authentication realm, capability, organization, team, group, parent-child and record-level scope; tests must cover wrong-role/wrong-org/wrong-family and revoked grants. Treat the original `charges.py` and `players_crm.py` audit risks as explicit design review inputs, never copy their access patterns.
2. **Browser sessions:** evaluate replacing JavaScript-accessible `mobile_token` localStorage with a server-managed browser session/BFF, with explicit CSRF defense; ensure native clients retain a suitable independent bearer flow. Never introduce a half-complete auth migration.
3. **Secret handling:** no credentials in Git; use narrowly accessible runtime secrets, rotation, redaction and secret scanning. Enforce HTTPS, security headers with CSP compatibility validation and bounded uploads.
4. **Production Docker:** adopt preview-tested least-privilege controls (`read_only`, `cap_drop`, `no-new-privileges`, suitable tmpfs, CPU/memory/PID and log caps) *with service-specific exceptions*; pin and verify deployed images, don't copy preview IPs/ports blindly. Keep Caddy.
5. **Database and recovery:** forward-only controlled migrations; restricted runtime role; indexed scoped queries; controlled backups including media, off-host retention and successful isolated restore drills. Preserve source untouched and quarantine irreconcilable imports with evidence.
6. **Background work:** durable PostgreSQL outbox/claim leases, retry limits, idempotency keys, metrics and concurrency/restart tests before enabling external side effects.
7. **Supply chain and CI:** locked restores, dependency audits, secrets and container scans, generated SBOM, tagged artifact provenance, contract drift, migrations, integration tests, role-boundary tests, visual/accessibility smoke and release approval.
8. **Performance and operations:** representative PostgreSQL `EXPLAIN` checks, load tests, response/error/queue metrics and trace correlation with PII redaction; only add Redis, brokers or other services if measurements justify them.
9. **Cutover:** record source and target commits, current live image/DB provenance, completed parity matrix, data counts/hashes, RPO/RTO, rollback rehearsal, provider sandbox tests and explicit human production approval.

## PR completion protocol

Each functional migration PR must include:

- Exact source file paths and pinned source SHA, routes and interactive states being reimplemented.
- Evidence for visual comparison (desktop/mobile, Greek language), API response/error/deep-link compatibility, roles/capabilities, and accessibility.
- PostgreSQL schema/migration with safe rollback/forward recovery notes and no silent data rewrite.
- Positive and negative authorization tests; runtime and frontend tests; locked build and CI evidence.
- Source-to-target record reconciliation where data is involved, including quarantined/unmapped variants.
- Explicit status: **source-only / implemented / local-tested / CI-passed / preview-verified / approved-production**. Do not infer one status from another.
- No direct production deploy, no merge into `main` without review, and no irreversible changes.

## Work order

1. Bring the source parity inventory up to today's pinned source snapshot; review newly changed routes and semantics.
2. Close authentication/browser-session and production-container hardening gaps in isolated, testable PRs.
3. Migrate core private **family → player → coach** journeys on existing identity/roster/calendar data.
4. Complete private management workflows and player CRM/evaluations, always with org and record policies.
5. Deliver finance/commerce with explicit sandbox gates, transactional accounting and provider idempotency.
6. Implement moderated communication/social/media, notifications and durable background jobs.
7. Port remaining public pages, marketing, engagement, reporting and non-critical features; verify visual and functional parity.
8. Complete full cross-role, device, browser, restore and performance certification before any live cutover.

**Explicit unknowns:** No live VPS access/production database census was performed here. The source repository is not the same as runtime deployments. Exact pixel parity, every API route's behavior and every data variant must be demonstrated, not presumed. This contract is the initial migration deliverable; it does not implement the missing feature modules.
