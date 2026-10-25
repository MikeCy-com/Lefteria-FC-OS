# Target architecture

Status: design for the parallel rewrite; not implemented. The owner selected the technology direction. Module boundaries and migration details remain subject to the full inventory.

## Principles

1. Start with a modular monolith: one codebase and PostgreSQL cluster, local transactions inside the application. Separate API and worker hosts only where lifecycle requires it. No business microservices without measured need.
2. Preserve behavior before improving it. Every feature, role, URL, API contract, integration, data field and mobile link maps to an owner, adapter, replacement or reviewed retirement.
3. Put business rules in typed server-side use cases and domain modules. UI owns presentation; API owns validation, permissions, finance rules and cross-entity workflows.
4. Use relational constraints and transactions for relationships and money. Use JSONB only for genuinely variable external payloads or transitional raw data.
5. Enforce authorization server-side and default-deny. Resolve role, organization, team, group and record ownership on every private operation.
6. Treat jobs as at-least-once. Add durable state, retries and idempotency before repeatable side effects.
7. Hide providers behind interfaces. Tests use fakes; live payments, messages, publishing, AI and backups require approved environments.
8. Make youth privacy, accessibility, recovery, logging and operating procedures part of acceptance criteria.

## System shape

- Frontend: SvelteKit + TypeScript. Use route/layout boundaries for public pages, CMS/admin and role-based mobile/PWA. Preserve existing /app paths, query tabs, notification destinations, public redirects and Greek UX. Evaluate SSR for SEO-sensitive pages and a Node adapter only after VPS/runtime, cache and privacy checks.
- API: ASP.NET Core + C# on .NET 10 LTS as the core business API. Typed request/response contracts and application services; domain logic depends on interfaces, not HTTP, ORM or providers. Publish versioned OpenAPI and generate the TypeScript client in CI.
- Compatibility: retain old /api contracts behind explicit adapters until all web, PWA/TWA and provider callers are inventoried, migrated and monitored.
- Database: PostgreSQL as rewrite system of record, with versioned migrations, foreign keys, constraints, indexes and transaction-safe finance. EF Core/Npgsql is the proposed default; use measured parameterized SQL for reporting/bulk paths.
- Jobs: .NET Worker Service for migrated reminders, notifications and long-running work. First evaluate a PostgreSQL outbox/job table with transactional enqueue, leases, attempt count, next-run and terminal failure state. Use locking and idempotency. Add Redis/broker only if observed throughput or isolation needs exceed this design.
- Integrations: adapter interfaces for Twilio SMS, Stripe, Web Push/VAPID, Meta, Google Drive, object storage and AI. Preserve provider contracts/keys during transition. Keep Python only for mapped AI/ML/media work where libraries give a concrete advantage.
- Media: retain FFmpeg and current storage contract initially. Define type/size/path validation, object keys, access checks, retention and backup.
- Operations: Compose for local/staging/self-hosted operation, private networks, health/readiness, non-root images, resource limits, named volumes, backup/restore and rollback. GitHub Actions builds immutable artifacts; production remains manual and approval-gated.
- Observability: structured redacted JSON logs with request/job correlation and OpenTelemetry where an operated collector/receiver exists. No secrets, child data, payment details or provider response bodies in logs.

## Module ownership

| Module | Owns | Must not own |
|---|---|---|
| Identity and access | Admin/customer/mobile identity mapping, password/OTP, login policy, role grants and scope resolution | Feature data or UI-only authorization |
| Organizations and facilities | Club orgs, first-team/academy context, locations, teams, groups and assignments | Duplicate credentials or free-text scope bypass |
| Academy/player care | Registrations, parent-child links, profiles, training, attendance, evaluations, development, drills and sessions | Finance ledgers or unreviewed public visibility |
| Football operations | Seasons, teams, rosters, fixtures, results, match events/statistics, standings, transfers and archives | CMS layout or provider calls |
| Finance/commerce | Charges, payment states, receipts, expenses, products/tickets, orders and reconciliation | Browser-held secrets or arbitrary balance mutation |
| Content/public site | News, club settings, sponsors, partners, gallery, approved editorial content and public visibility | Canonical ownership of players, fixtures or finance |
| Community/communications | Chat, announcements, polls, feed, reactions, notifications and delivery tracking | Authorization copied from client state |
| Media/video | Upload metadata, protected delivery, FFmpeg work, annotations, clips and lifecycle | Unvalidated paths or uncontrolled public access |
| Audit/operations | Append-only business/security audit, health, migrations, backups and job monitoring | Secrets or unrestricted personal-data dumps |

Use explicit application interfaces/events between modules. PostgreSQL schema-per-module is a candidate to make ownership visible inside one database; validate cross-schema constraints and migration ownership before adopting it.

## Contracts, identity and authorization

- OpenAPI is the contract source. Endpoints define typed schemas, validation, auth requirements and stable error shapes. Generated TypeScript client output is not hand-edited; CI detects drift.
- Track API versioning/deprecation, caller traffic and old client compatibility. Never remove a route just because its replacement exists.
- Map admin, customer and mobile principals before designing a canonical identity schema. Import stable legacy IDs; do not merge accounts only because email/phone match.
- Preserve OTP, remember-device durations, multiple-device behavior and deep links during transition. Any new cookie/session policy needs explicit compatibility and security tests. Document copied-token and logout semantics.
- Store role grants and scope links explicitly. Test endpoint permission, organization, team/group, player, parent-child and owner boundaries. Public and youth responses need privacy tests.
- Rate-limit login, OTP and password recovery. Use uniform auth errors, secret-safe logs and append-only audit records. Record high-impact account, role, scope, finance, restore, publishing and export changes transactionally.

## Data, work and deployment

- Import from a safe export into isolated PostgreSQL using stable legacy IDs, field-level transforms and resumable checkpoints. Reconcile counts, links, money, privacy and historical records. Source Mongo remains untouched.
- Decide bulk freeze/final import versus change capture after write patterns are known. Avoid dual-write until consistency/idempotency are justified.
- Write domain change and outbox entry in one transaction. Worker retries use backoff, idempotency, persisted attempts and visible terminal failures; webhook signatures/replays are verified.
- Compose exposes only reviewed reverse proxy. API, worker and PostgreSQL are internal. Configuration/secrets are external. Separate staging and production data and credentials.
- CI restores locked dependencies, formats/lints/types, tests, verifies OpenAPI/client drift and migrations, builds frontend/API/containers and scans dependencies/images. Staging and production release gates are explicit.

## Cutover acceptance

No production switch until evidenced:

- Every feature, route, role/scope, integration, collection and public/mobile URL has a mapped owner and parity result.
- All protected operations have server-side positive/negative authorization tests; no unresolved P0/P1 security issue.
- Two isolated import rehearsals show zero unexplained record, relationship, privacy or financial-total differences.
- Payment/webhook replay, refund, receipt and reconciliation flows are safe and duplicate charges are excluded.
- PWA/TWA login, push subscription/deep links, install/update and accessibility work on supported devices.
- Worker retry, concurrency, idempotency, restart recovery and terminal failure behavior pass.
- PostgreSQL and media restore plus application rollback are demonstrated in staging; owner-approved RPO/RTO met.
- CI and staging smoke checks pass; performance is compared with captured old-system baselines.
- Release notes, operator runbook, rollback owner and explicit approval are recorded.
