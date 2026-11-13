# HUB Payabl sandbox and individual weekly learning — staging runbook

**State:** development branch only. Not production-approved and not deployed.
**Scope:** Payabl fan subscriptions, fan topic learning, and weekly in-app notices.

## Invariants

- The **fan** role never becomes a verified academy parent or player because they select a topic or purchase a subscription. There is no child-profile or guardian-permission grant.
- Fans choose parent, player, or both topics; the latter starts **two independent Lesson 1 tracks** and two independent 7-day schedule clocks.
- An active subscription is checked **server-side on every lesson read and completion** against the original subscriber identity, organization, checkout record, Payabl-captured transaction, expiry, and original topic selection.
- A Payabl return/redirect, an initiating checkout, an unsigned client success message, a pending capture, or an arbitrary user-provided amount **never** grants HUB access. Paid subscription status requires the existing signed callback and bound finance ledger.
- Refunds/reversals recorded by signed Payabl events revoke access. Expired subscriptions require another **manually initiated** sandbox checkout.
- No card number, CVV, raw callback signature, bank data or mobile token goes into lesson notifications or analytics.
- Only currently published, human-approved, independently reviewed, age-unrestricted articles are eligible.
- New users receive the oldest eligible approved article first. User A's progress never changes the content User B receives upon joining.
- A scheduled in-app notification contains only an ID and generic `lesson_ready` event; article text is fetched through fresh authorization.
- No outward push, SMS or email lesson messages are sent by this first worker. Integrating actual push requires a separately verified provider/consent workflow.

## Isolated staging operator sequence

1. Isolate staging Payabl credentials and HTTPS origin. Keep `Payabl:Sandbox=true`; enable `Payabl:Enabled` **only** in the test environment. Do not use live merchant credentials.
2. Apply explicit staging migrations via the existing operator-only migration procedure. Never run migrations at normal API startup or against Hostinger live databases.
3. Provision `hub.fan_plan_prices` for intended organization/track with an operator-approved EUR amount in **cents**, and separately set `is_enabled=true` after sign-off. The migration intentionally seeds **no** prices or enabled plans.
4. Ensure the mobile runtime has only the SQL function grants in `ops/postgres/identity-app-grants.sql`; do not grant direct access to `finance.payment_requests`, `hub.fan_subscriptions`, checkout links, answer keys or delivery tables.
5. For automated weekly in-app delivery, provision a dedicated PostgreSQL role with `CONNECT` and `USAGE ON SCHEMA hub`, **without raw table privileges**; apply `ops/postgres/hub-fan-lesson-worker-grants.sql` with `fan_lesson_worker_role`. Only then set `LEFTERIA_FAN_LESSON_WORKER_CONNECTION` and `LEFTERIA_FAN_LESSON_DELIVERY_ENABLED=true` in isolated staging.
6. Verify a fan with both interests can complete parent Lesson 1 while player Lesson 1 remains unfinished. Check a second fan registering later starts with Lesson 1 of their own selected topics.
7. Exercise callback replay, mismatched order, duplicate capture, refund/reversal, expiry, price-change-before-confirmation, disabled plan, failed checkout and role-swap flows. A browser redirect must never mark the subscription active.
8. Verify weekly notices are idempotent after worker restart and that a cancellation/revoked role removes access to queued content.
9. Re-run all five exact-head CI jobs and isolated PostgreSQL E2E acceptance checks **before any merge or staged deployment**.

## Current limitations and dependencies

- The Payabl adapter is **sandbox checkout**, not recurring stored-card billing. Renewal is manual. The initial access period is 30 days, not automatically a calendar-month charge.
- Fan content is enabled only after an operator sets explicit plan prices and sandbox credentials. No default monetary amount is invented.
- The seven-day schedule begins when each member opens the authenticated learning experience and is enrolled; member-registration-triggered enrollment and mobile push delivery are separate work.
- Separate parent/player academy learning schedules are developed in PR #238. PR #241 must be reconciled after PR #238, and both require passing CI.
- GitHub Actions jobs currently fail before runner assignment. See issue #240. **No merge, production deployment or provider activation while blocked.**
