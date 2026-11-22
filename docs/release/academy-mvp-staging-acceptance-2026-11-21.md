# Academy-first MVP: isolated staging acceptance record (2026-11-21)

Status: **NOT RUN / RELEASE BLOCKED**. This is an execution and evidence record, not evidence of passing checks. Never use the legacy production Hostinger containers, DB, certificates, DNS, or real families. No production deployment is authorized here.

## Candidate and environment evidence

- Source repository: `MikeCy-com/Lefteria-FC-OS`.
- CMS PR #417 merged as `2b4ae253d8ac2576b0f33c9047d4f23a904a166a` after exact-PR-head Rewrite build and Browser UI visual audit succeeded. A merge SHA is **not** an exact-main CI or preview-image verification.
- Staging hostname: NOT VERIFIED; preview Compose project: `lefteria-os-preview` (documented target, not observed); container IDs, network, database host/name, PostgreSQL volume, object storage, secrets, provider endpoints and feature flags: NOT VERIFIED.
- Separate environment owner/operator, date/time, redacted command outputs, and release image manifest/SHA256SUMS: NOT RECORDED.
- All evidence below must be gathered against the **same pinned candidate SHA and isolated preview environment**.

## Stop conditions (G0)

Before issuing **any** mutating command, independently compare the preview hostname/DNS, project name, Docker container IDs, volumes, PostgreSQL host and DB, TLS path, image labels, credential sources, callbacks and backups against the legacy production inventory. Stop if any connection, mount, credential, or external provider overlaps production or cannot be demonstrated distinct. Synthetic people and fake financial data only. Do not put tokens/passwords, children's details, DB dumps or private financial rows in CI artifacts or GitHub.

The existing authoritative procedure is `deploy/preview/README.md`; follow it for pinned image provenance, root-only environment management and safe migration. A deployment of a new commit must not silently overwrite existing staging configuration. No destructive down migration on CMS tables with content/audit history.

## Manual read-only staging HTTP evidence (after G0, not a deployment)

GitHub Actions → **Academy isolated staging HTTP preflight (read-only)** is
`workflow_dispatch` only; it does not run on a push, create a release, or
modify a running container. Before triggering it, an infrastructure operator
must independently prove **G0** in a reviewed issue and explicitly confirm
that the nominated HTTPS origin is a separate preview with no shared
production writes, secrets, providers, databases, mounts, or callbacks.
The checked preview hostname alone is never proof of isolation.

Provide the exact **current** main SHA, explicitly approved isolated staging
origin (without credentials or a URL path), the reviewed G0 GitHub issue URL,
and tick the false-by-default isolation confirmation. The workflow rejects
missing confirmation, stale main, missing exact-main build/browser CI and
non-isolated/prod URL forms before issuing GETs.

The check inspects readiness, public/admin HTML, backend-disabled high-risk
routes, and anonymous denial of protected Academy/guardian/roster APIs.
It records HTTP statuses in a short-lived Actions artifact. If the preview
is behind a Basic/other external access gate, this no-secret Actions job may
correctly report BLOCKED; an authorized operator can run the same
`ops/release/staging-mvp-preflight.mjs` from a controlled machine using
the opt-in `LEFTERIA_PREVIEW_GATE_AUTHORIZATION` environment value.
Never paste credentials into issue comments, workflow inputs or artifacts.

The report **does not** verify deployed SHA, private five-role sessions,
guardian revocation, financial reconciliation, media/document isolation,
staging backup/restore or rollback. These remain **NOT RUN** until separately
observed against synthetic records. An HTTP result is not production GO.

## Image revision evidence is separate from HTTP readiness

After G0 approval and an authorized staging-only rollout, independently capture
the running preview API/web image IDs and OCI revisions and compare them to
the immutable, SHA-256-verified preview bundle using the **offline**
[staging runtime provenance guide](preview-runtime-image-provenance-2026-11-22.md).
This operation reads only minimal sanitized Docker fields and requires a
separate operator-signed evidence record. It does not connect to Hostinger,
change Docker resources, or replace G0 and actual five-role acceptance.
Neither a correct manifest nor a successful HTTP preflight alone proves the
running revision.

## Executable acceptance matrix

| ID | Scenario and expected observable result | Evidence needed | Status |
| --- | --- | --- | --- |
| G0 | Verify *independent* preview VPS services, Compose project, DB, TLS, volumes, media paths, webhooks and disabled providers | Redacted inventory and two-person signoff | NOT RUN |
| CI | Exact **main** candidate SHA: Rewrite build, browser audit, API contract, ASP.NET, SvelteKit, PostgreSQL identities, Docker; verify release manifest labels and checksum | Workflow URLs, SHA, manifest digest | NOT RUN |
| DB | Back up staging; inspect archive and independently restore into disposable, isolated PostgreSQL; compare counts/schema/checksums | Archive checksum, restore exit code, comparative probes, cleanup | NOT RUN |
| MIG | Run only the versioned migrations and reviewed role grants against isolated staging; prove runtime permissions | Migration IDs, `identity-app-permissions-check.sql`, `web-admin-auth-permissions-check.sql`, denied-table probes | NOT RUN |
| CMS-1 | Academy Editor creates draft; anonymous/public API cannot see it; Editor cannot publish; First Team owner cannot read it | HTTP statuses and redacted security audit | NOT RUN |
| CMS-2 | Academy Manager publishes, only Academy public URL/nav/sitemap updates; edit draft without republishing leaves public snapshot identical | Before/after public snapshots, audit IDs | NOT RUN |
| CMS-3 | Version race, revoked session, wrong-owner forged request and failed manager capability deny; unpublish removes public result without erasing draft | 403/409 responses, audit proof and published-row projection | NOT RUN |
| GEO | Super Admin sees all Academy cities/sites; city manager sees only own city; location manager only own site; each coach only assigned groups | Separate synthetic role sessions; denied requests | NOT RUN |
| FIN-1 | With synthetic invoices, check issued/paid/outstanding, cash receipt and card settlement totals by city/site against ledger; unallocated issues must not disappear | Redacted totals, invariant comparison, ledger event IDs | NOT RUN |
| FIN-2 | Re-run `ops/postgres/academy-location-finance-checks.sql`, `ops/postgres/finance-global-role-checks.sql` and relevant POS cashier checks in a disposable PostgreSQL fixture; demonstrate forged cross-location denial | SQL probe exit codes and denial assertions | NOT RUN |
| FIN-3 | Payment provider remains sandbox/disabled until signed callbacks, idempotency, refunds and receipts reconcile; never treat browser return as payment | Provider sandbox proof and non-duplicate entries, or disabled-feature proof | NOT RUN |
| MEDIA | CMS image URL and ownership, safe HTTPS/local source, alt text and publication separation; no private youth asset in public media browser | Public response and denied asset requests | NOT RUN |
| REC | Snapshot staging DB and media; rehearse app image rollback; restore disposable DB and prove schema/finance/access invariants; if a non-reversible migration exists choose tested forward recovery, not destructive down migration | Recovery timestamps, image IDs, restore diff, operator signoff | NOT RUN |
| OBS | Health endpoint, errors, logs, monitoring/alerts and synthetic identity checks; safe shutdown/disable switches | Observation window, links and thresholds | NOT RUN |

## Required evidence protocol

For each row record: `candidate_sha`, `preview_environment_id`, `executed_at_utc`, `operator`, exact redacted command/test identifier, `PASS/FAIL/BLOCKED/NOT RUN`, observed result and private evidence location. Record failure/denial paths as well as success. Never mark PASS based solely on source-level tests. Store secrets and backup archives only in protected infrastructure, not this repository.

For finances, verify *both* accounting identities and arithmetic: issued minus reversals minus settled payments equals remaining balance, with refunds and duplicate callbacks separately accounted for; compare each permitted location's subtotal with the authoritative server ledger and the cross-site aggregate. If legacy/unallocated entries cannot be assigned with proof, classify them explicitly as reconciliation exceptions. Do not invent missing payment records.

## Restore and rollback decision

1. Before migrations, take and checksum root-only preview DB/media backups; prove archive can be restored into a disposable **different** database and cross-check table counts, ledger totals and user/permission boundaries.
2. Capture previous immutable image IDs, env/config integrity fingerprints (never contents), schema migration ID, container start times and production read-only inventory.
3. Rehearse old-image rollback on isolated preview **only** with a compatible database; if schema is incompatible, use a tested forward fix or full **staging-only** restore of the prior snapshot, with rehearsed recovery time and data-loss window.
4. Confirm canonical CMS published snapshots and financial ledgers after recovery, re-run G0, verify no production service/config changed, and require two-person signoff.

## Release decision

**NO-GO until all required Academy MVP gates in `ops/release/check-go-live.mjs --profile=academy-mvp` are verified against the exact release SHA, and an authorized staging operator has attached the evidence above.** Use `node ops/release/check-go-live.mjs --profile=academy-mvp --require-ready --sha=<exact-sha>` as the final non-deploying check. A passing check does not itself deploy or authorize production cutover.

After staging passes, require a separate written production cutover approval, verified protected backups, explicit rollback ownership, health observation and payment/provider authorization. Never infer release readiness from CI green alone.
