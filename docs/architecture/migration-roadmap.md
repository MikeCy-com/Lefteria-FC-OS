# Migration roadmap

A gated parallel migration plan, not a production release schedule. The existing Lefteria-FC application and Mongo data remain authoritative. No production data or credentials were used for this design.

## Phase 0 — Audit and architecture (current checkpoint)

Deliver current-state, target, folder, technology, roadmap, risk and ADR documents. Pin source commit; keep the rewrite branch isolated.

Exit: evidence/unknowns are clear; no claim that deployed source equals GitHub main; selected stack and boundaries are reviewable. The architecture checkpoint is established. A minimal infrastructure foundation has started in Phase 2 while source inventories continue; domain behavior remains gated on Phase 1.

## Progress snapshot — 2026-10-25

- Phase 0 architecture documents are present on the rewrite branch.
- Phase 1 source audit has expanded to route counts, auth realms, authorization risks, PWA/TWA, media, backups, jobs and deployment. It is still incomplete: route-by-route permission mapping and field-level data mapping remain open.
- Phase 2 now has locked npm/NuGet dependencies, a generated API client, passing local .NET 10 and SvelteKit builds, health startup checks and a production web packaging smoke check. Earlier Actions runs failed before any steps started; Docker/Compose execution still requires a working runner.
- The relevant mobile identity contract has been mapped. Its role/session/OTP/organization policy core and typed navigation are implemented with focused checks. HTTP login, persisted grants, ownership relations and operational datasets are not migrated yet. The accepted feature queue is recorded in docs/implementation/feature-queue.md.
- Do not start feature-domain implementation until the relevant Phase 1 route, permission, data and caller mappings are complete. Foundation work can continue in parallel.

## Phase 1 — Complete inventories and contracts

1. Map features, routes, roles/scopes, data, callers and tests.
2. Enumerate Mongo collections, indexes, document variants, IDs, ownership, history, retention and uploads. Use code/schema inspection and sanitized data only if separately authorized.
3. Freeze representative API responses/error shapes and web/mobile deep links.
4. Audit every protected API route for role, organization/team/group, player and parent-child scope; map security fixes.
5. Inventory jobs, side effects, retries, integrations, backups and restore paths.
6. Establish isolated critical-path tests, build times and performance baselines.

Exit: every route and feature has behavior, owner, authorization, migration status and parity plan. Production untouched.

## Phase 2 — Repository foundation and vertical slice

Create SvelteKit/TypeScript and ASP.NET Core/.NET 10 projects, locked dependencies, formatting/static checks, tests and CI. Add secure Compose and empty isolated PostgreSQL. Establish migration policy, OpenAPI/client generation, configuration validation, health/readiness and structured redacted logs. Select a first module only after Phase 1 mapping.

Current foundation files provide a .NET 10 API host with typed development-only OpenAPI, bounded PostgreSQL readiness, a generated TypeScript client, a SvelteKit Node-adapter shell, internal networks and a Caddy edge. Local API/frontend builds, policy/navigation checks and isolated host smoke checks pass. Database migrations, worker jobs, real PostgreSQL integration/restore and observed container results remain outstanding.

Exit: clean build from fresh checkout, API/client drift check, PostgreSQL integration checks, readiness and green CI; no secrets in artifacts.

## Phase 3 — Identity, scope and audit

Map admin/customer/mobile principals and OTP flows. Verify password hash portability; map roles and organization/team/group/facility scopes. Implement disable/recovery, login audit, rate limits, safe errors and explicit session policy.

Exit: positive and negative authorization tests per role/scope, parent-child privacy, concurrent devices, 30/60/90-day behavior and legacy cookie/token compatibility. No credential changes or production account edits.

## Phase 4 — Football and academy

Migrate orgs, teams/groups, players, staff, registrations, seasons, fixtures, schedules, attendance, transfers/archives, evaluations and development. Add public reads and admin/PWA journeys; retain privacy filters and old links.

Exit: records/relationships reconcile; cross-org/group access is denied; player, calendar, match and attendance journeys pass.

## Phase 5 — Finance, commerce and content

Implement finance and commerce with decimal-safe amounts, explicit state transitions, receipts, audit and idempotent webhooks. Migrate news, settings, sponsors/partners, gallery/media, forms, marketing/events and public pages.

Exit: charge/order/payment/refund/receipt totals reconcile; webhook replay is safe; editorial and privacy behavior pass; URLs/contracts remain compatible.

## Phase 6 — Community, providers and worker jobs

Port chat, announcements, polls, feed, reactions, POTM, scoped push, Twilio, VAPID, Meta, Drive backup, storage, FFmpeg/video and verified AI workflows. Move scheduled work one job at a time; preserve VAPID keys/subscriptions.

Exit: adapters have fakes/sandboxes; retries are idempotent; no duplicate side effects; worker restart/lease recovery and privacy policies pass.

## Phase 7 — Data migration rehearsals

Define field mappings and provenance; create versioned, resumable, idempotent import tooling. Rehearse twice against isolated sanitized snapshots; produce counts, orphan, scope/privacy and finance reconciliation. Decide freeze/final import versus change capture only after write behavior is known. Document source/target backup, recovery and rollback.

Exit: zero unexplained data/money discrepancies, restore proven, rollback rehearsed, no open P0/P1.

## Phase 8 — Staging and operator rehearsal

Use separate staging data and credentials. Verify DNS/TLS, resource use, backup/restore, health, telemetry, provider sandbox, TWA/PWA, runbooks and release artifacts. Staging is not production.

Before cutover, present parity matrix, CI/security/data evidence, backup restore, performance comparison, release notes, rollback procedure and operator sign-off.

## Phase 9 — Production cutover, only after explicit approval

Back up and verify source/target, freeze or reconcile writes under the approved strategy, smoke-test critical flows, monitor outcomes and retain a rollback owner/trigger. Do not delete or downgrade source Mongo data. Production traffic/data changes require a separate explicit approval; this document is not deployment authorization.
