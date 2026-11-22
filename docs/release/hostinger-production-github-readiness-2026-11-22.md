# Hostinger production readiness — GitHub preparation record (2026-11-22)

**DECISION: NO-GO. GitHub preparation only. No production approval, deployment, cutover, domain change, migration, service restart, provider enablement, or customer-data copy is authorized by this document.**

**Repositories:** OS: `MikeCy-com/Lefteria-FC-OS`; original/live app: `MikeCy-com/Lefteria-FC`.
**Current reviewed source baseline:** main SHA `845e4c1a2172e6385e9f1b1fcc0b9ef56d0ebc95` when inspected on 2026-11-22. This is a snapshot, not an automatically approved release candidate.
**Mobile UI candidate:** draft PR #444 (`f9ca9eb20ff53ca6f09926cd5ce0cf1077333518`), with passing PR-head Rewrite build and Browser UI visual audit. **This is not main and not a production release.** Exact-main CI, live staging acceptance and image provenance must be repeated on the chosen final main commit.

## Existing repository controls — do not bypass

| Control | Repository entry point | Observed on 2026-11-22 |
| --- | --- | --- |
| Read-only production release preflight | `.github/workflows/production-release-preflight.yml` | Manual `workflow_dispatch`, `contents:read` and `actions:read` only; verifies exact current main SHA and release approval; no deployment |
| Independent exact-main CI | `ops/release/verify-exact-ci.mjs` | Requires latest successful *main push* Rewrite build and Browser UI jobs, checks complete job set, rejects stale/PR SHA |
| Human gate | `ops/release/verify-release-authorization.mjs` | Requires verified manifest and 5 distinct signoffs: club owner, finance, privacy, infrastructure, HUB editor |
| Release blockers | `ops/release/go-live-gates.json` | BLOCKED by default, including identity delivery, parent permissions, finances, backups and rollback |
| Production authorization record | `ops/release/operator-release-approval.json` | `status=blocked`, `candidate_sha=null`, no signoffs |
| Immutable *preview* packaging | `.github/workflows/preview-images.yml` | Builds main-push CI-verified image archives/checksums, **does not deploy** |
| Isolated staging procedure | `deploy/preview/README.md` | `lefteria-os-preview` project with independent DB, volumes, secrets, edge; immutable image checks |
| Cutover process | `ops/release/MVP-CUTOVER-RUNBOOK.md` | Operator-controlled GO/NO-GO; separate production decision |

**Important:** The preview bundle is **not** automatically a production-deployable artifact. Production image promotion, platform, routing, secrets, persistent data paths and rollback need a reviewed, pinned and explicitly authorized operator procedure. Do not create an always-on GitHub→Hostinger production SSH pipeline at this stage.

## G0 — two-environment read-only Hostinger identity and separation

This is **NOT RUN**. An authorized operator must record the following **privately** with timestamps and evidence for both environments, before any mutating Hostinger action:

| Field | Legacy production | Isolated OS staging | Pass condition |
| --- | --- | --- | --- |
| Hostinger account/VPS ID, public hostname/IP | NOT VERIFIED | NOT VERIFIED | Independently identified; ownership clear |
| Reverse proxy, listeners, DNS, TLS certificate and callback destinations | NOT VERIFIED | NOT VERIFIED | No unapproved production route, cert, webhook or public traffic change |
| Compose project and full container IDs / image digests / start times | NOT VERIFIED | NOT VERIFIED | Distinct process identities and exact pinned image provenance |
| Docker networks, PostgreSQL/MongoDB hosts, databases, volumes and mounts | NOT VERIFIED | NOT VERIFIED | No shared writable data; no migration against source |
| Media/object-storage and backups | NOT VERIFIED | NOT VERIFIED | Separate staging writes; no private youth data in GitHub |
| Secrets, roles, service-account origins and providers | NOT VERIFIED | NOT VERIFIED | Secrets remain private; no cross-env reuse; staging live-Payabl/SMS disabled |
| Prior image/config fingerprints, recovery ownership and restoration test | NOT VERIFIED | NOT VERIFIED | Reversible, rehearsed, evidence retained privately |

**Fail closed:** Any unknown/matching writable mount, container/service, credential, source DB, origin or certificate path blocks deployment. Store private inventory/credentials in access-controlled Hostinger/operator storage, *not* issues, Actions artifacts or commits. A GitHub PR badge cannot establish G0.

## Staging acceptance — must precede promotion

- Merge only reviewed, passing changes. Prepare an **exact main push** SHA and repeat all six mandated CI jobs (five Rewrite jobs plus Browser UI audit) at that SHA.
- Verify checksums, archive manifest, `org.opencontainers.image.revision`, source label, architecture and loaded image IDs under the isolated preview procedure.
- Back up **staging only**, prove archive restore to a disposable separate database, then review migrations and runtime least-privilege probes before rolling out an authorized staging change.
- Validate running `/app` and admin UI/API against the **deployed** revision, not merely the checked-out tree.
- Execute real authenticated Parent, Player, Coach, Management and Fan journeys against approved synthetic accounts. Verify cross-family, team, FC/Academy, city and site **server-side 403/401** denials; expired/revoked sessions, role switching and back/refresh/deep-link behavior.
- Capture 320×640, 390×844, 768×1024 and desktop screenshots and compare with the owner's original mobile PNG reference pairs. Absence of originals means visual parity **UNVERIFIED**.
- Validate scope-driven Academy/First Team isolation, CMS publishing, media/youth consent, SMS/Payabl disabled-state, finance/POS restrictions, PWA updates, logs/alerts, backups, restore and staging rollback.
- Record operator, UTC time, tested commit, private evidence location, actual HTTP/error results and post-check production inventory invariants. A synthetic CI preview is **not** staging.

Detailed APP matrix: `docs/release/app-five-role-staging-acceptance-2026-11-22.md` in PR #444. Wider Academy matrix: `docs/release/academy-mvp-staging-acceptance-2026-11-21.md`.

## Production preparation — required before owner GO

1. **Choose actual release scope.** Evaluate `academy-mvp` vs `full`. Unported or unsafe finance/POS, live Payabl, Twilio, subscription, CMS/social actions must fail closed at the **API/edge**, not only hide buttons. Owner and operational signoff required for Cyprus VAT, receipts, refunds, cash custody and payments.
2. **Account/data continuity.** Reconcile original MongoDB record counts and IDs with PostgreSQL, parent-child ownership, cities, locations, first-team boundaries, historical balances/documents/assets and quarantines. No unexplained mismatches or unapproved live imports.
3. **Production-grade identity.** Test sign-in, real authorized OTP delivery/recovery, role grants, revocation, private media and youth consent without bypasses or hardcoded test codes.
4. **GitHub environment governance.** Confirm protected `main`, required Rewrite build + Browser UI checks, no direct force pushes, reviewed PRs, signed or auditable release tags, restricted Actions permissions. These repository settings are **NOT VERIFIED** and cannot be inferred from workflow YAML. Do not add SSH credentials or production API tokens to broad repo-level CI contexts; use environment-scoped secrets after formal approval and reviewer protection.
5. **Artifact promotion.** Agree on exact immutable image digest, expected architecture, provenance and a documented rollback-compatible schema. Rebuild/tag only via reviewed release process; never deploy mutable `latest` or assume PR-head archives qualify.
6. **Backups, change window and rollback.** Secure independent encrypted DB/media backups and verify a distinct-host/volume restore; rehearse reversible routing and old-image fallback on staging. Record RTO/RPO, rollback triggers, operator contacts and audit/monitoring window.
7. **Final approval.** Populate only independently confirmed gates with dated non-secret evidence linked to the exact reviewed **main** SHA. Five required distinct signoffs and owner production authorization. Keep Payabl live disabled until separately approved and tested.
8. **Execution:** Any eventual Hostinger production command/DNS change requires a separate explicit owner-approved cutover procedure; do not invoke it from the preflight workflow. Verify production service health, UI/API, rollback readiness and no regression at the actual deployed image revision.

## Read-only GitHub checks (no Hostinger access or deployment)

From an authorized CI runner or local checkout of the **current main** commit:

```sh
node --test ops/release/*.test.mjs
node ops/release/verify-exact-ci.mjs --sha=<current-main-sha> --require-passing
node ops/release/verify-release-authorization.mjs --sha=<current-main-sha> --profile=academy-mvp --require-ready
```

The final command requires read-only GitHub Actions access via `GITHUB_TOKEN` and is **expected to fail** until every evidence and signoff gate passes. Alternatively, manually dispatch the read-only `Production MVP release candidate preflight` on `main` using its exact current SHA. A successful preflight alone does not authorize executing production commands.

## Current decision / owners

- **COMPLETED:** Repo-side read-only control inventory and non-secret launch preparation checklist.
- **IN PROGRESS:** APP PR #444 awaiting staging visual + role acceptance, independent release design review.
- **FAILED:** None claimed in this preparation document.
- **BLOCKED:** G0 Hostinger isolation; verified staging deploy SHA and five real role sessions; source-data reconciliation; backup/restore and rollback; original screenshot reference pairs; fiscal and live payment decisions; exact-main release CI and five named approvals.
- **DEPLOYMENT:** None. Live Hostinger production MUST remain unchanged.
- **NEXT:** Obtain authorized **read-only** production/preview inventory evidence, reconcile preview provenance, complete staging, and only then select an immutable main release candidate.
- **APPROVAL REQUIRED:** Written owner authorization for any future production deployment; independent owner/finance/privacy/infrastructure/HUB signoffs; separate merchant and fiscal approvals.

**ONE TEAM ● ONE DREAM**
