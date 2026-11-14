# Folder structure and module ownership

Status: **implemented layout inventory verified at OS main `01f4d339` (2026-11-14)**. The original proposed tree below is a historical target, not a statement of today's filesystem. Follow the current ownership map before moving code.

## Implemented layout and ownership (verified 2026-11-14)

The implemented OS is **not** the uncreated generic "Lefteria.Api/Modules"
layout shown further below. It is a .NET modular monolith with four actual
projects and a SvelteKit frontend. Moving code to match the historic proposal
without migrating imports, generated contracts, runtime paths and integration
tests will break the working application.

| Implemented path | Current owner and purpose | Move/rename policy |
|---|---|---|
| `frontend/src/routes/` | SvelteKit URL ownership: public routes, `/admin`, `/app` and explicitly mounted endpoint routes | Preserve public URLs, deep links, search params and legacy redirects |
| `frontend/src/lib/public/` | Public site shell, images, navigation, types and styling | Refactor components internally; keep SEO and image fallbacks intact |
| `frontend/src/lib/admin/` | Admin workspace, editors, role-aware components and theme | Do not infer grants from navigation; API checks permissions |
| `frontend/src/lib/mobile/` | Five mobile roles, controllers, navigation and app-specific components | Preserve auth, role switching, guardian scope, notifications and PWA paths |
| `frontend/src/lib/api/` | Generated OpenAPI bindings and typed shared client | Never hand-edit generated `schema.d.ts` |
| `frontend/src/lib/server/` | Server-only public projection and upstream clients | Keep private environment/config out of browser bundles |
| `backend/src/LefteriaFc.Api/` | ASP.NET Core endpoints and request composition | Keep endpoint logic thin; no direct DB/provider operations |
| `backend/src/LefteriaFc.Application/` | Application workflows/contracts | Must not depend on HTTP UI or infrastructure implementation |
| `backend/src/LefteriaFc.Domain/` | Domain authorization and core rules | Remains independent of persistence and transport |
| `backend/src/LefteriaFc.Infrastructure/` | PostgreSQL persistence, data access and adapters; EF migrations | Migrations append-only; add new migrations instead of rewriting history |
| `backend/tools/` | Separate preview, import and diagnostic executables | Never mount preview/demo bootstrapping into live production |
| `backend/server.py` | **Workspace-only** Python ASGI launcher/proxy for the fixed development runtime | Not the production Python backend; preserve existing workspace launch contract |
| `scripts/workspace/` | Workspace runtime, demo seed and mirror import utilities | Keep runtime secrets and restored live data out of source |
| `scripts/parity/` and `docs/parity/` | Source-to-OS feature/API/UI inventories and comparison evidence | Mark progress only with contract and test evidence |
| `deploy/preview/`, `deploy/production/` | Isolated deployment configuration, guards and runbooks | No unreviewed public edge or production changes |
| `ops/postgres/` | Role grants, isolated SQL probes and policy checks | Never weaken RLS or grant broad table access |
| `contracts/openapi.json` | Exported .NET API contract | Regenerate when endpoints change; regenerate TS client too |

### Verified SvelteKit route families

- **Public:** `/`, `/team`, `/academy`, `/academy/groups`,
  `/academy/registration`, `/news`, `/player/[id]`,
  `/staff/[id]`, `/events`, `/partners`, `/sponsors`,
  `/checkout`, `/shop`, `/match/[id]`, etc.
- **Administration:** `/admin` with real subroutes for academy office,
  attendance, announcements, club, documents, evaluations, finance, groups,
  Hub, leads, news, POS, schedule and trials.
- **Role application:** `/app/[...path]` routes five mobile roles through
  their role-aware shell; `/app/login` is a separate entry.
- **Legacy compatibility:** `/cart`, `/register` and `/profile` are
  intentional server redirects and **must not be counted as implemented
  e-commerce/profile journeys**.
- **Public API:** ASP.NET Core owns `/api/public/*` and
  `/api/uploads/{folder}/{fileName}`. The SvelteKit
  `routes/api/public/news/+server.ts` handler is a fixed-origin
  direct-Node-preview BFF. The workspace bridge and reviewed preview edge
  route `/api/public/news` directly to .NET; they do not exercise that BFF.
  Any removal or relocation must first update its isolated BFF tests and
  verify both routing modes.

### Safe reorganization checklist

Before moving any existing route/component: (1) capture its current import
callers, URL and query/deep-link contracts; (2) compare the legacy
behavior/visibility; (3) identify the actual current owner above; (4) make the
smallest behavior-preserving move, adjusting SvelteKit imports and source maps;
(5) run frontend typecheck, tests, .NET policy/integration checks and visual
audits; (6) record mapping and acceptance evidence in `docs/parity/`.
No file should be moved solely because the historic proposed tree has a
different directory name.

## Historical proposed tree (not the implemented filesystem)

    .
    ├── AGENTS.md
    ├── README.md
    ├── frontend/
    │   ├── package.json
    │   ├── package-lock.json
    │   ├── svelte.config.js
    │   ├── vite.config.ts
    │   ├── src/
    │   │   ├── app.html
    │   │   ├── hooks.server.ts
    │   │   ├── lib/
    │   │   │   ├── api/                 # generated client wrapper
    │   │   │   ├── auth/                # typed session helpers, not domain authorization
    │   │   │   ├── components/          # accessible shared presentation
    │   │   │   └── features/
    │   │   │       ├── academy/
    │   │   │       ├── football/
    │   │   │       ├── finance-commerce/
    │   │   │       ├── content/
    │   │   │       ├── community/
    │   │   │       └── media/
    │   │   ├── routes/
    │   │   │   ├── (public)/
    │   │   │   ├── (admin)/
    │   │   │   └── app/                 # role PWA and deep links
    │   │   └── service-worker.ts
    │   ├── static/
    │   └── tests/{unit,e2e}/
    ├── backend/
    │   ├── Lefteria.sln
    │   ├── Directory.Build.props
    │   ├── Directory.Packages.props
    │   ├── src/
    │   │   ├── Lefteria.Api/
    │   │   │   ├── Program.cs
    │   │   │   └── Modules/
    │   │   │       ├── Identity/
    │   │   │       ├── Organizations/
    │   │   │       ├── Academy/
    │   │   │       ├── Football/
    │   │   │       ├── FinanceCommerce/
    │   │   │       ├── Content/
    │   │   │       ├── Community/
    │   │   │       ├── Media/
    │   │   │       └── Audit/
    │   │   ├── Lefteria.Worker/
    │   │   └── Lefteria.Contracts/       # stable shared primitives only
    │   └── tests/{Unit,Integration,Contract,Authorization}/
    ├── contracts/
    │   ├── openapi/
    │   └── typescript-client-config/
    ├── database/
    │   ├── migrations/
    │   ├── import/
    │   └── scripts/
    ├── infrastructure/
    │   ├── compose/
    │   ├── containers/
    │   └── runbooks/
    ├── docs/{architecture,operations,security,migration}/
    └── .github/workflows/

Keep modules as folders inside the API project until isolation has measured value. Avoid a project per entity. Worker may reference application contracts but not controllers or UI. Generated client output comes only from contracts/openapi.

## Legacy-to-target mapping

| Current source | Target owner | Migration notes |
|---|---|---|
| frontend/src/App.js, club-site, public pages | SvelteKit public routes and content/football/academy features | Preserve URLs, Greek content/design, SEO, redirects and privacy filters. |
| frontend/src/pages/AdminPanel.jsx and frontend/src/pages/admin | SvelteKit admin routes and capability features | Split shell from feature pages; enforce all permissions in API. |
| frontend/src/mobile and /app routes | SvelteKit app routes and mobile features | Retain role journeys, TWA/PWA lifecycle, query tabs, push destinations and back behavior. |
| frontend/src/context and RememberDevice.jsx | frontend/src/lib/auth | Preserve admin/customer/mobile semantics and remember-day rules. |
| frontend API utilities and scattered Axios calls | frontend/src/lib/api wrapper over generated client | Migrate call-by-call; no duplicate contract source. |
| backend/server.py decorators/startup hooks | API composition, module endpoints and Worker | Composition only; no domain rules or DB queries in Program.cs. |
| backend/routes/* | Corresponding Identity, Academy, Football, FinanceCommerce, Content, Community, Media or Audit module | Map every route/caller/scope; retain old contract until migration is proven. |
| backend/models.py | Module domain records and API DTOs | Separate persistence/domain/wire types; preserve aliases/defaults/null behavior in contract tests. |
| backend/database.py and Mongo queries | Module persistence, SQL migrations and import tools | Map relationships, indexes, history and mixed legacy forms; no blind JSON-to-table conversion. |
| backend/routes/scheduler.py and startup jobs | Worker plus durable job/outbox schema | Migrate individually; make delivery idempotent and process-safe. |
| finance, charges, Stripe routes | FinanceCommerce and Stripe adapter | Preserve amount precision, lifecycle, webhook idempotency, receipts and audit. |
| videos, drills, storage | Media and Academy; optional Python worker | Retain FFmpeg, media IDs, privacy and secure upload/delivery. |
| docker-compose.yml and Dockerfiles | infrastructure/compose and containers | Publish edge only; add .NET, Node and PostgreSQL without changing old production deployment. |
| old GitHub Actions | .github/workflows | Add locks, client drift, migrations, security scans, container build and staged release gate. |

## Dependency and ownership rules

1. Endpoints call application use cases; they do not query EF contexts directly or call providers.
2. Application workflows depend on domain abstractions and external interfaces, not ASP.NET, SvelteKit or concrete vendors.
3. Domain logic has no database, HTTP, storage or UI dependency.
4. Infrastructure implements persistence and adapters; it does not call web routes or modify another module's tables outside a documented contract.
5. Cross-module work uses application contracts/events. Direct SQL across module schemas needs a transaction/ownership rationale.
6. Finance, identity and audit cannot be modified through generic content CRUD.
7. Svelte components render and collect input; the API owns authorization and data scope.
8. OpenAPI-generated client output is deterministic and not manually edited.
9. Migrations are append-only and reviewed. Import scripts are isolated from runtime API and production by default.
10. Test domain/use cases, PostgreSQL integration, API contracts, authorization and PWA journeys at their boundaries.


## Initial bootstrap notes (historical; see implemented map above)

The current implementation includes:

- backend/src/LefteriaFc.Api: ASP.NET Core host, health/readiness endpoints and development-only OpenAPI.
- backend/src/LefteriaFc.Infrastructure: EF Core/Npgsql registration and PostgreSQL context boundary.
- backend/src/LefteriaFc.Domain: mobile role/session/OTP and organization scope policies.
- backend/tests/LefteriaFc.PolicyChecks: focused dependency-free policy acceptance checks.
- contracts/openapi.json and frontend/src/lib/api: exported API schema, generated TypeScript and the shared typed fetch client.
- frontend/src/lib/mobile/navigation.ts and frontend/tests: source-compatible role navigation and client checks.
- frontend/src/routes: SvelteKit server-rendered shell.
- compose.yaml and deploy/Caddyfile: single public edge proxy; API, web and PostgreSQL use segmented internal networks; only the proxy is host-published. The worker itself is not implemented yet.
- .github/workflows/rewrite-build.yml: locked restores, policy/client/navigation checks, API contract drift, production web packaging and isolated container health checks.

This initial bootstrap summary is preserved for design history. The current implementation has many additional modules and routes; refer to the verified implemented map above. Do not add domain stubs merely to fill folders.
