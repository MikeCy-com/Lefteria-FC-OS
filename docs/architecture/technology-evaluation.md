# Technology evaluation

The owner selected the replacement direction below. These are target decisions, not completed migrations. Each still has compatibility, data, security and hosting gates.

## Stack comparison

| Current | Target | Benefit | Burden and migration gate |
|---|---|---|---|
| React 19/JavaScript, CRA/CRACO | SvelteKit/TypeScript | Typed routes/components and structured installable web app; SSR where useful. | Rebuild every user journey; verify SEO, accessibility, service-worker updates, push, Android TWA, URLs and Node hosting. |
| FastAPI/Python core API | ASP.NET Core/C# on .NET 10 LTS | Typed modular core for identity, club workflows, finance and APIs. Microsoft lists .NET 10 support through 2028-11-15. | Rebuild routes/rules; prove host/runtime fit and migrate auth without login/permission regressions. Keep Python for verified AI/media advantages. |
| MongoDB/Motor | PostgreSQL | Relational constraints and transactions for finance, memberships, parent links, academy and club operations. | Highest data risk: mixed historical documents, identifiers, personal data and provider state require mapping and reconciliation. |
| Scattered Axios calls | OpenAPI plus generated TypeScript client | Single typed contract and consistent validation; enable drift/deprecation checks. | Pin contract generator; no generated churn or exposure of internal endpoints. |
| FastAPI startup scheduler | .NET Worker Service and durable PostgreSQL queue/outbox where needed | Persisted retries and idempotent work; isolate scheduling from web process without adding a broker immediately. | Requires leases, retry/dead-letter visibility and provider idempotency; one-job-at-a-time migration. |
| Existing Docker deployment | Docker Compose | Familiar reproducible local/staging/self-hosted runtime. | Confirm VPS supports .NET 10, Node SSR, PostgreSQL backups and required capacity; existing Compose has public port mappings. |
| Manual checks/deploy | GitHub Actions | Automated locked restore, static checks, tests, builds, migration checks and controlled artifacts. | Separate PR/staging/production secrets and runner limits; production stays manual and approval-gated. |
| Existing FFmpeg | Keep FFmpeg | Proven media-processing tool. | Preserve codecs, metadata, validation, storage lifecycle and CPU limits; verify each actual pipeline. |
| Web Push/VAPID | Keep | Avoid replacing current approach and re-permissioning devices. | Preserve private key/subscription contract; test browsers, PWA/TWA and deep links. |
| Twilio | Keep behind SMS adapter | Business logic no longer depends on one vendor. | Preserve OTP templates, rate limits and delivery result handling; no new paid provider. |
| Stripe, Meta, Google Drive, object storage | Retain behind adapters during parity work | Reduce cutover risk and protect checkout, publishing, backup and media behavior. | Verify provider configs/accounts; sandbox, replay/idempotency, export and fallback plans. |
| Python AI/ML dependencies | Provider-independent AI interface; selective Python | Swap providers/local models without coupling core business rules; keep concrete library advantage. | Actual production call sites, data sensitivity, evaluation, cost and licensing are not fully inventoried. No new recurring model provider without approval. |
| Existing logging | Structured logging + OpenTelemetry | Correlate HTTP and job failures and measure performance. | Redact personal data/secrets; define retention and collector operation; no paid telemetry service by default. |

## Cost, licensing, operations and security

- .NET 10 is the selected LTS line. Microsoft lists support until November 15, 2028; keep SDK/runtime on supported monthly servicing updates. [Microsoft release support policy](https://learn.microsoft.com/dotnet/core/releases-and-support)
- The selected core is open-source. The exact license and vulnerability status of every direct/transitive package must be checked from locked dependency files before release; produce an SBOM. No new commercial service or recurring subscription is part of this design.
- PostgreSQL adds schema/migration, backup, restore and capacity duties; it does not replace application validation, authorization or recovery tests.
- SvelteKit SSR adds a Node process plus cache, authentication and privacy decisions. Choose a Node adapter only if hosting and cache tests pass. Public content, admin and PWA data need distinct cache policies.
- EF Core/Npgsql is the proposed default persistence path, not a required answer for all queries. Verify provider/EF version compatibility and migration workflow before adoption. [Npgsql EF Core documentation](https://www.npgsql.org/efcore/)
- Compose fits a single self-hosted system. Kubernetes, service meshes and infrastructure-as-code are deferred until infrastructure complexity justifies them.
- Actions build/validate; they must not deploy every push to production. Secrets are environment-scoped and unavailable to untrusted PR jobs.
- Existing Emergent Object Storage and a private Python package index are operational dependencies. Inventory data export, SDK contract, license, cost and fallback before changing either.

## Deferred choices

- No Redis or separate message broker initially. Revisit from measured queue lag, throughput, lock contention and availability.
- No microservices before a capability has an independent scaling/release/security need.
- No broad AI agent rewrite until provider use, data sensitivity, evaluation and cost are documented.
- No CMS/page-builder redesign until editorial ownership is inventoried.
- No provider replacement for Stripe, Twilio, VAPID, Meta, Google Drive, object storage or FFmpeg in parity phase.
- No final hosting-cost estimate until VPS capacity, media storage, backups, bandwidth and provider costs are known.
