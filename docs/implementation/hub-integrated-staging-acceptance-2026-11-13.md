# Integrated HUB / AI Agent staging acceptance gates

**Status:** test plan, not evidence that the flows have passed. 2026-11-13. Applies only to an isolated Lefteria-FC-OS preview. Do not use live Lefteria FC / Hostinger production, production Payabl credentials, actual minors' records or production domains.

## Non-negotiable environment gate (G0)
Before running any migration, login, worker or payment test, an operator must independently confirm and record:
- Preview hostname, DNS target, VPS/service identity, Compose project, network, database server and database name, PostgreSQL volume/mount, object-store bucket, credentials, webhook URLs, and API/AI provider account.
- Every identifier is distinct from production; preview has no mounted production secrets/volumes, no shared writable media, no production callback endpoints, and no production-origin allowlist.
- Separate test identities, synthetic players/guardians/coaches/fans and age bands. Never test with personal data from live academy users.
- Snapshot/restore of preview database and object store works; rollback is rehearsed against preview only.
- Deploy uses a pinned commit with passing **exact-head** CI (frontend, API, API contract, PostgreSQL permissions/migrations and Docker); record commit SHA, workflow URLs, operator, time and evidence.
- A kill switch exists for worker/provider spending, publishing, checkout and notification delivery. Default disabled until explicitly authorized.

**Stop immediately if any environment boundary is ambiguous. No fallback to production.**

## Release-blocking test matrix
Record each case as **PASS / FAIL / BLOCKED / NOT RUN**; attach synthetic fixture IDs, HTTP/DB assertions, screenshots where appropriate, test commit and audit-event IDs. A source unit test alone is not an end-to-end pass.

| Gate | Scenario and expected assertions |
| --- | --- |
| E1 Editorial scope | Editor can view own-org drafts, previews and revision; cannot access other organization. Mobile identities and unauthenticated users cannot obtain drafts, bodies or preview media. |
| E2 Human-only approval | AI worker can create only moderated draft; cannot review, approve, publish, change role or invoke privileged functions. Text and actual image reviewed separately by authorized humans, with image integrity, moderation, rights/alt text and revision proof checked. |
| E3 Revision races | Edit after review invalidates both approvals. Stale version, revoked editor, switched organization, rejected asset and unauthorized manager all fail closed and generate audit trails. Approved is not published; explicit manager publication is required. |
| E4 Published reader | Parent sees only own-org published parent material; player only own-org published age-eligible player material after canonical age proof. Unknown age excludes age-banded content. Other audience, organization, drafts, expired/removed revision and images are denied by server, not just hidden in UI. |
| E5 Education pathways | Verify age-stage and role-specific suggested content, learning order, accessibility and language in Greek/English. Changing roles/accounts invalidates cached content. Recommendations reorder **only already authorized** items. No fan read granted by interest selection. |
| C1 Coach authorship | Verify active coach/team/player assignment at compose and send; observation and final message explicitly signed off by authenticated coach. AI draft cannot impersonate coach or auto-send. |
| C2 Private feedback delivery | Only intended active player can retrieve feedback; parent/fan/other player/other-org and revoked coach cannot. Duplicate send, concurrent edit, identity switch and expired session fail closed. Audit identity, target, revision and release time. |
| V1 AI-assisted visuals | Generate into quarantined private store under preview provider budget; scan/sanitize; verify MIME, dimensions, content hash, attribution/license, alt text and moderated content. Reviewer sees *actual asset*, not just textual description. Rejection/failed scan cannot publish or expose signed URLs. |
| F1 Fan plan selection | Authenticated fan chooses parent-interest, player-interest or both **as topics only**. Never gains a player/guardian role, family profile, coaching notes, announcements or invoices. No charge when changing interest. |
| F2 Payabl sandbox | Create order using server-controlled EUR amount, currency, organization, fan subject, plan and invoice/payment-request binding. Checkout must be sandbox and no real card charged. Verify webhook signature, timestamp/replay policy, merchant/order IDs, amount/currency and authenticated return handling against provider's current contract. |
| F3 Entitlement lifecycle | Only server-verified settled charge activates audience-scoped, expiring entitlement. Browser return, pending, declined, wrong amount, duplicate webhook, refund, chargeback, cancellation, expiry, revoked identity and wrong organization must not grant access. Cover idempotent concurrent callbacks. |
| F4 Restricted fan reader | Published fan-eligible article served only after fresh server entitlement and selected audience check. Test unauthenticated and every other role, URL guessing, asset download, entitlement revocation mid-session and cached response leakage. No children's private material. |
| S1 Cross-role integrated E2E | Synthetic manager -> AI draft -> separate reviewers -> manager publish -> authorized parent/player learning flow; coach private signed message; fan sandbox purchase -> verified entitlement -> fan-only content. Verify no cross-role state reuse on logout/account switch. |
| S2 Isolation and recovery | Disable AI and Payabl feature flags, sever provider network, retry failed jobs, validate no partial rights or message deliveries, restore preview snapshot and reproduce results. Prove production service metrics/configuration remain unchanged (read-only comparison). |

## Evidence and go/no-go
- Every test row needs a named owner, actual recorded result and evidence link. **Any FAIL, BLOCKED or NOT RUN is a no-go for live enablement.**
- Capture redacted audit records and tests of denied paths, not only successful screenshots.
- Confirm user-facing privacy notices, opt-ins, unsubscribe paths, data-retention/deletion controls, image licenses and translated editorial copy are reviewed.
- Confirm Payabl merchant-specific signature fields, sandbox settlement/refund behavior, lawful recurring-charge consent and callback endpoints with Payabl; do not infer compatibility from a mock.
- Confirm whether player age derives from an authoritative team/academy record; never derive sensitive age access from a user-selectable client stage.
- A preview green run is **not** permission to deploy to production. Production cutover requires separate written authorization, rollback runbook, reconciled imported data, protected backups and observed health gates.

## Immediate engineering sequence
1. Merge the editorial workspace PR only after its exact-head CI and security review; fix current blockers first.
2. Add server-side canonical age/role proof and publish-only education learning path reads; test content version invalidation.
3. Complete coach signoff, private delivery permission tests and audit.
4. Implement quarantined visual generation/preview proof with independent human review.
5. Introduce Payabl sandbox-only fan entitlement reader and revocation; keep fan paywall locked until verified.
6. Execute this matrix end-to-end in isolated staging with recorded evidence before considering live cutover.

Nothing in this document authorizes running migrations, turning on providers, changing DNS or touching production.
