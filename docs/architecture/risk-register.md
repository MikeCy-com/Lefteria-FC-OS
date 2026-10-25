# Risk register

Initial assessment: 2026-10-25. Likelihood and impact are qualitative until Phase 1 and VPS/data evidence. Open means unresolved, not necessarily a live production defect.

| ID | Risk | Likelihood / impact | Evidence and exposure | Mitigation and release gate | Status |
|---|---|---|---|---|---|
| R-01 | Lost feature/business behavior | High / Critical | Large React/FastAPI app, many role/mobile journeys and historical fixes. | Feature-route-role-data-test matrix; parity tests and owner review per slice. No cutover with unmapped critical flow. | Open |
| R-02 | Mongo data loss/semantic corruption | High / Critical | Schemaless documents and mixed legacy fields; complete collection census absent. | Field mapping, stable legacy IDs, resumable importer, two rehearsals, orphan/privacy/count reports; source untouched. | Open |
| R-03 | Finance or payment inconsistency | Medium / Critical | Charges, manual payment state, Stripe webhooks, orders and receipts. | Transactional state machine, decimal-safe values, unique idempotency/provider keys, replay tests and amount reconciliation. Sandbox only. | Open |
| R-04 | Authorization/youth privacy regression | High / Critical | Parent-child, player, coach, org and group scope; prior audit recorded gaps on older baseline. | Fresh route audit, deny-by-default, positive/negative scope tests, youth redaction; no unresolved P0/P1. | Open |
| R-05 | Login/session breakage or takeover | Medium / Critical | Admin/customer/mobile JWT, OTP, remember-device and multiple-device requirements. | Map principals/callers; verify hash portability; preserve limits, safe errors, key handling and session behavior; rehearse compatibility. | Open |
| R-06 | Duplicate/lost/stuck jobs | High / High | Scheduler runs in FastAPI deployed with two workers; module task state is process-local. | Audit claims and side-effect ordering; durable leases/outbox, idempotency, backoff, failure visibility, concurrent/restart tests. | Open |
| R-07 | Internal services publicly exposed | Medium / Critical | Source Compose maps Mongo 27017 and API 8001 to host; Mongo URL has no credentials. Firewall/reachability unknown. | Rewrite publishes edge only; private services and least privilege; inspect VPS separately before operations. | Open |
| R-08 | Backup incomplete or restore fails | Medium / Critical | Mongo/Drive backup, DB volume, local uploads and object storage are separate paths; restore not verified. | Inventory coverage/retention/encryption/RPO/RTO; restore DB and media in isolated staging. | Open |
| R-09 | PWA/TWA or external client breaks | Medium / High | /app URLs, query tabs, push links and Android TWA; new API/auth origins. | Compatibility routes/adapters, device matrix, push/install/update/deep-link tests and old-route monitoring. | Open |
| R-10 | VPS cannot support .NET, Node SSR and PostgreSQL | Medium / High | Existing self-hosting is Python/Mongo/static frontend; capacity/runtime unknown. | Inspect hosting limits; benchmark/restore in staging; resource bounds; defer SSR if unsupported. | Open |
| R-11 | Provider unavailable, costly or behavior changes | Medium / High | Stripe, Twilio, VAPID, Meta, Drive, Emergent storage and AI dependencies are documented; live configs unverified. | Adapters, fakes/sandboxes, contract/cost/license review and export/fallback plan. No new paid service without approval. | Open |
| R-12 | Sensitive data leaks from API/log/export | Medium / Critical | Child/player identity, phone/email, attendance, chat and finance. | Data classification, least privilege, redaction, retention, export audit, sanitized non-prod and privacy tests. | Open |
| R-13 | GitHub main differs from deployed production | Medium / Critical | Main SHA verified; deployed SHA/VPS not accessed. | Obtain approved read-only deployment provenance before migration/cutover assumptions. | Open |
| R-14 | Dependency/supply chain/non-repeatable build | Medium / High | Legacy source has a private Python index/external tarball. Rewrite now locks npm/NuGet and pins runtime lines and Actions commits. SBOM/image scanning is still outstanding. | Keep locked restores and vulnerability gates; add SBOM/image scanning before release. | Open |
| R-15 | Overengineering before evidence | Medium / Medium | Risk of adding brokers, Redis, microservices, generic CMS or AI infrastructure during stack change. | Modular monolith and PostgreSQL outbox first; add only by measured need and ADR. | Controlled |
| R-16 | Exposure of player payment documents | High / Critical | Static review at 5111f06 found public GET handlers in charges.py accepting caller-supplied player/charge identifiers without mobile identity or parent-child ownership dependencies. Runtime exposure was not probed. | Map the handlers and callers; require authenticated ownership/scope, add negative authorization coverage, and review existing production exposure separately. | Open |
| R-17 | CRM operations rely on authentication without explicit capability | High / High | Static review at 5111f06 found players_crm.py handlers whose local guard checks authenticated-user presence but does not use the canonical permission helper. | Define capabilities for list/transfer/reactivation and enforce role, organization and record scope server-side. | Open |
| R-18 | Frontend dependency resolution was not reproducible | Medium / High | package-lock.json is now generated and reviewed; npm ci is used by CI and Docker. NuGet graphs also have lockfiles. | Keep lockfiles reviewed and require locked restores. | Resolved |
| R-19 | Container/CI verification unavailable | Medium / High | Initial Actions jobs failed before any steps ran. Local .NET 10 and SvelteKit builds and startup/policy checks now pass; Docker is unavailable locally. | Obtain a real container/Compose health run and determine runner availability. | Open |
| R-20 | Initial SvelteKit graph had advisories | Low / Medium | The 2.x graph was reported for GHSA-pfhg-q3qv-9q6r and GHSA-27mw-82g8-phjj plus old cookie. Scaffold migrated to patched stable SvelteKit 3.1.0, adapter 6, Vite 8 and TypeScript 6 using official migration guidance. Final npm audit reports zero vulnerabilities. | Keep locked versions and audit gates current; no forced dependency downgrade or ignored peer checks. | Resolved |
| R-21 | Bootstrap OpenAPI dependency was vulnerable | Medium / High | First full restore caught NU1903 for Microsoft.OpenApi 2.0.0 (GHSA-v5pm-xwqc-g5wc). API package graph now pins patched Microsoft.OpenApi 2.13.0 and ASP.NET OpenAPI 10.0.13; strict restore/build pass. | Keep transitive auditing enabled and locked versions current. | Resolved |

## Release blockers

- Any unexplained critical data mismatch, finance discrepancy, cross-scope access or youth-privacy exposure.
- No verified database/media restore or usable rollback/forward-recovery plan.
- Unknown ownership or unhandled legacy class among migrated data.
- Deployed revision mismatch that changes assumptions.
- Live side effects before sandbox, idempotency and replay checks.
- Production cutover without separate explicit approval.
