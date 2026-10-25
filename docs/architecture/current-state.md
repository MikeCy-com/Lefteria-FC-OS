# Current-state audit

Audit date: 2026-10-25  
Source: [Lefteria-FC](https://github.com/MikeCy-com/Lefteria-FC)  
Baseline: main at 5111f06af5d8b17f855ceed9637caca02db117e2  
Status: initial evidence-backed inventory; full route authorization and data-shape audits remain open.

## Scope and confidence

The source repository metadata and main commit were read from GitHub. Reviewed evidence includes the product requirements, security policy, Compose configuration, frontend package, backend requirements, authentication/database modules, API entry point, selected features and GitHub Actions workflows. This is a source snapshot, not proof of deployed code or live VPS configuration.

The older CMS/RBAC/location audit calls itself an initial inventory and uses baseline 7b22a7592241fe4e05b280c3885cc9341c934dd3. Revalidate its findings against the current head before implementation. No production database, secrets, live provider account or VPS was accessed.

## Stack and deployment

| Area | Observed on source main |
|---|---|
| Web and CMS | React 19, JavaScript/JSX, Create React App/CRACO, React Router 7, Tailwind 3 and Yarn 1. App.js and AdminPanel.jsx are large orchestration files, with feature pages also extracted. |
| Mobile | Role-based web app under /app, installed as a PWA and packaged as Android TWA artifacts per current product docs. Existing URLs, query tabs, notification links and device behavior are compatibility requirements. No separate React Native app was identified in reviewed docs. |
| API | FastAPI 0.110.1, Python 3.11, Pydantic and route modules. backend/server.py is a large composition root importing route setup functions. |
| Persistence | MongoDB 7 via Motor/PyMongo. backend/database.py creates the client from MONGO_URL and DB_NAME. Documents are schemaless; code carries much of the relationship and scope behavior. Complete collection/index census not done. |
| Identity | Separate admin/customer/mobile flows use password hashes, OTP, JWT and cookie/bearer contracts. Admin records are reloaded from Mongo on requests and inactive users rejected. Product requirements describe stateless tokens, concurrent devices and selectable 30/60/90-day remember-device behavior. Local logout does not revoke a copied bearer token. |
| Integrations/media | Backend image installs FFmpeg. Requirements include Stripe, Twilio, VAPID, Google/OpenAI/LLM/Hugging Face libraries. PRD identifies Stripe Checkout, configurable Twilio SMS, VAPID push, Meta publishing, Google Drive backups and Emergent Object Storage. Runtime use of each AI dependency is not mapped. |
| Compose | Mongo, backend and frontend services; named Mongo/uploads volumes; certificate mount. Backend image starts Uvicorn with two workers. |
| CI | quality-gates.yml runs locked Yarn install, frontend tests/build, Python 3.11 install, compileall and backend unit tests. social-network.yml adds focused social authorization/UI checks. No migration, staging, restore, end-to-end or container-security gate was verified in these workflows. |

## Feature inventory

This list is based on current PRD and selected source paths, not a complete endpoint or parity matrix.

| Capability | Identified behavior |
|---|---|
| Public site | Greek club, team/player/staff profiles, academy, fixtures, standings, reports, seasons/archives, news, events, sponsors/partners, trials/registration, contact, shop and POTM voting. Legacy redirects must remain compatible. |
| CMS and operations | Admin users; academy/team/player/staff; transfers/archives; fixtures and match reports; news/settings; sponsors/partners; media/gallery; marketing/events; security/audit/backup; analytics. Editorial page layouts/navigation are partly code-controlled; this is not a general page/block CMS. |
| Football and academy | Teams/groups, rosters, registration, training, attendance/RSVP, evaluations/development, fixtures/results, match events/statistics, archives/transfers, drills and video analysis. |
| Finance and commerce | Charges including recurring/bulk operations, receipts/invoices, expenses/reports, products/tickets, carts/orders and Stripe checkout/webhooks. Some admin flows include manual payment state changes. |
| Mobile and community | Parent, coach, player, management and fan journeys; calendars, chat/attachments, announcements/reactions, polls, reels, fan feed, MVP/POTM and targeted push. Youth visibility, parent-child and team/group scope are material rules. |
| Media and video | Image/video uploads and delivery, clips, annotations/markers, skill timelines, drill metadata and drill snapshots attached to training. Object storage and local uploads have separate operational paths. |
| Background work | Training/match reminders, due-payment reminders and PWA update notifications run from the FastAPI process. Retry and cross-worker guarantees need review. |

## Code and module shape

A prior remote source survey at the same main head recorded approximately 6,555 lines in backend/server.py, 209 route decorators, 53 imported route modules, 80 Pydantic models, 2,334 lines and about 49 routes in frontend/src/App.js, 4,541 lines in AdminPanel.jsx and 3,703 lines in SocialNetworkPage.jsx. These are concentration indicators, not quality measures.

Selected reviewed paths include frontend/src/App.js, backend/server.py, backend/models.py, backend/database.py, backend/auth.py, backend/routes/scheduler.py, docker-compose.yml and backend/requirements.txt. See the current PRD and mobile/drill audit documents for additional behavior notes.

## Security and operations observations

1. The source security policy requires secrets from environment/secret store, no public Mongo/API ports, authenticated MongoDB, explicit CORS origins, server-side authorization, OTP/login rate limits, safe logs, validated uploads and tightly gated seeding.
2. Checked-in Compose maps host ports 27017 and 8001 and sets MONGO_URL without credentials. This is an exposure risk if used as-is; actual firewall/reachability is unknown. The rewrite will publish only a reviewed edge service and keep API, worker and PostgreSQL private.
3. The earlier RBAC/location audit recorded uneven use of its permission matrix and gaps in normalized location/resource modeling, but has an older baseline. A fresh route-by-route and scope-by-scope review is mandatory.
4. Background reminders run within FastAPI while the image starts two Uvicorn workers. Module task state is per process. Existing sent markers may help idempotency but do not by themselves prove cross-process claims. Review send/update ordering before porting jobs.
5. Mongo backup, Google Drive configuration, local upload volumes, object storage, retention and restore order must be audited as one recovery system. Compose volumes do not prove backups are restorable.
6. Current data includes children/academy players, parent links, attendance, communications and financial records. Collection counts, variants, indexes, ownership and retention are not fully inventoried.
7. Existing CI has frontend tests/build and backend compile/unit tests. E2E, vulnerability, migration, restore and deployment-control coverage remains unverified.

## Open audit items

- Actual deployed SHA, VPS firewall, TLS/reverse proxy, runtime versions, secrets, backup state, restore test and service health were not verified.
- Enumerate Mongo collections, validators/indexes, document variants, ID formats, relationships and personal-data flows. Use safe schema/code inspection; do not fetch production data for this audit.
- Map every API route to authentication, role, organization/team/group scope, validation, audit event and web/mobile caller.
- Identify live uses of AI/media dependencies and current provider/account/configuration dependencies.
- Build route-feature-data-role-test compatibility matrix and capture response contracts/deep links.
- Measure current build/tests, critical page/API performance, backup size/age and restore time using safe environments.

## Evidence links

- [Source main snapshot](https://github.com/MikeCy-com/Lefteria-FC/tree/5111f06af5d8b17f855ceed9637caca02db117e2)
- [Requirements and current status](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/memory/PRD.md)
- [Security requirements](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/memory/SECURITY.md)
- [Compose](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/docker-compose.yml)
- [Quality gates](https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/.github/workflows/quality-gates.yml)
- [Earlier CMS/RBAC/location audit](https://github.com/MikeCy-com/Lefteria-FC/blob/7b22a7592241fe4e05b280c3885cc9341c934dd3/docs/full-cms-rbac-location-current-state.md)
