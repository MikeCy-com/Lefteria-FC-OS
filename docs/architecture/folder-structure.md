# Folder structure and module ownership

Status: proposed. No application directories are implemented by this document.

## Proposed tree

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
