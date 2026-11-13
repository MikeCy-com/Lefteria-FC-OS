# HUB Payabl sandbox checkout and personal weekly education — implementation boundary

## Implemented on development PR #242 (not deployed)

The OS now has an **opt-in, staging-only** backend path for:
- An independent learning enrollment per verified mobile identity, organization and audience. The first visit to the in-app HUB starts that individual's enrollment at **Week 1**. It does **not** backdate the clock to a prior registration event. Members who join months later still start at Week 1; existing learners never reset.
- An authenticated private schedule/inbox for parent and player curricula and separate fan-parent / fan-player interest tracks. For fan curricula, every enrollment and every read checks the **current active verified Payabl subscription** for that audience. A fan choosing both tracks will have independent delivery clocks.
- A transaction-serialized, idempotent weekly SQL dispatcher. It selects the **oldest eligible approved published lesson not previously delivered**, checks current phone/roster/guardian/fan entitlement, and advances the enrollment exactly one week. If an approved lesson is unavailable, it waits without skipping the position. A late dispatch does not send a burst of missed weeks.
- A background dispatcher running every 10 minutes **only** if a dedicated staging database, a dedicated `lefteria_hub_scheduler` credential and the explicit isolated-staging feature flags have been configured. The default in all environments is OFF. It creates **in-app** deliveries; native push/SMS/email are not implemented here.
- Sandbox-only fan Payabl **initial checkout**: hosted card checkout through the existing payabl. adapter, fixed server-configured EUR prices for parent, player or both interests, explicit consent, opaque callback nonce, signed-provider event parsing, idempotent payment intents and duplicate-pending-charge protection.
- Payabl callback events become **review_required** for independent settlement verification. They **never** set a fan subscription to `active`, `verified_at`, or grant delivery rights. Browser redirects, unsigned amount/state fields, and checkout creation are not payment proof.

All payment records and lesson outbox tables are private. The mobile API role may call only narrow, per-identity functions. The scheduled worker has a separate operator-provisioned SQL role with a single EXECUTE grant, no raw table permissions.

## Test staging configuration (never live)

Operator requirements:
1. Isolated OS staging environment, `ASPNETCORE_ENVIRONMENT=Staging`.
2. Isolated database ending in `_staging`, `_preview` or `_test`, and reviewed .NET migrations applied with the migration owner. Never use the original Lefteria FC database or Hostinger production.
3. Existing Payabl sandbox adapter configured with approved test merchant credentials, HTTPS preview origin and no real-money merchant checkout.
4. A dedicated login `lefteria_hub_scheduler` with no role memberships, superuser/admin/BYPASSRLS powers or raw HUB table privileges; apply the reviewed `ops/postgres/hub-weekly-scheduler-grants.sql` with the migration owner and `-v scheduler_role=lefteria_hub_scheduler`.

```dotenv
# Sandbox ONLY; never set in production.
Payabl__Enabled=false
Payabl__Sandbox=true
Payabl__MerchantId=
Payabl__Secret=
Payabl__PublicOrigin=
HubFanPayabl__Enabled=false
HubFanPayabl__ParentMonthlyCents=0
HubFanPayabl__PlayerMonthlyCents=0
HubFanPayabl__BothMonthlyCents=0
HubLearningScheduler__Enabled=false
HubLearningScheduler__IsolatedStaging=false
HubLearningScheduler__ConnectionString=
```

Prices are **EUR cents** and can be configured only on the server. No client-supplied payment total is accepted. Secrets must be supplied through a staging secret store, not Git.

## Unfinished release gates

- **Do not turn on production Payabl or automatic recurring subscriptions.** Actual recurring mandates, card vault/token handling, expiry/cancel/chargeback processing and merchant-specific payment method approval are not implemented.
- An authenticated Payabl settlement enquiry or separately audited reconciliation workflow must bind actual merchant account, capture transaction ID, order ID, EUR amount, payment method, expiry and subscriber before a subscription can transition from pending to active. Signed callback alone is insufficient; no activation function is installed by this PR.
- Verified fan checkout activation and account self-service cancellation/renewal still need implementation. The current fan preview remains locked until a subscription is independently active and verified.
- True at-registration schedule creation (rather than first HUB visit), admin pause/resume controls, notification push/email, quiz score gating and mobile billing checkout form need follow-up implementation.
- CI must complete five successful jobs on the **exact PR head**: ASP.NET Core, SvelteKit, API contract, PostgreSQL permission/concurrency tests, Docker Compose. The new API requires regenerating `contracts/openapi.json` and `frontend/src/lib/api/schema.d.ts` once GitHub Actions executes. Current hosted Actions runs fail before a runner starts; see issue #240.
- Disposable database E2E must verify fan status changes, expired subscriptions, late registrations, cross-role access, callback replay, partial/failed settlement, duplicate requests and content withdrawal. The source-level tests do **not** constitute this evidence.

## Explicit safety

No original `MikeCy-com/Lefteria-FC` repository changes; no production Hostinger deployment; no autonomous money movement. A staging setting must not be repurposed as production approval.
