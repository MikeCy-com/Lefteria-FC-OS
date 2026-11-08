# payabl. integration — sandbox foundation (2026-11-08)

## Source, scope and current status

Implemented **only** on `feature/payabl-payments-foundation` in Lefteria FC OS. This is **not** a switch of `lefteriafc.cy`, a connection to the existing Stripe account, or a live provider merchant integration. No production money movement has been activated and the old system remains authoritative.

Provider documentation: [Hosted Payment Page](https://docs.payabl.com/docs/initiate-a-transaction-1), [request and notification signature](https://docs.payabl.com/docs/signature-calculation), [CFT payouts](https://docs.payabl.com/docs/credit-fund-transfer-credit-cards). Confirm account-specific API endpoints, signature scheme, callback delivery and approved transaction categories with payabl. before activation.

The new **Admin → Οικονομικά → payabl.** view supports private, administrator-scoped sandbox hosted checkout requests in EUR. Only `manager` and `super_admin` with an explicit organization grant may create or list these requests. Application endpoints re-check the current admin session and database definer functions re-check the same session and grant with a revocation lock. Frontend-only permissions never authorize money movement.

## What is implemented

| Feature | State |
|---|---|
| Private admin sandbox payment link creation | Code implemented; requires migration, grants, approved test merchant and verification |
| HTTPS hosted checkout; no PAN/CVV handled in OS | Implemented gateway adapter |
| Payment request audit and unique client idempotency | Private DB migration and audited, locked functions |
| Signed asynchronous webhook | SHA-256 signature, time window, opaque callback nonce and exact order-ID match |
| Merchant refund and partial refund | Not enabled; needs settlement ledger, reversal API and reconciliation |
| Parent/player/academy recurring tuition | Not enabled; needs guardian consent, mandates/tokenization and retry/dunning contract |
| Website shop/order/POS settlement | Not enabled; needs authoritative orders, inventory/amount snapshots and POS orchestration |
| Card/wallet payment methods | Determined by merchant configuration at payabl. Hosted Payment Page |
| Employee cards and salary payments | Not enabled; payout eligibility, beneficiary verification, approvals, HR/payroll checks and payout-specific contract required |
| Bank transfers, bulk salary batches, treasury or settlement reports | Not enabled; requires banking product/account API approval |
| Disputes, chargebacks, automated accounting and reconciliation | Not enabled; separate provider-state and settlement import needed |
| Go-live/production | Not approved or connected |

## Configuration (secret store, never commit keys)

Settings bind to ASP.NET configuration section `Payabl` (environment variables use double underscore):

```dotenv
Payabl__Enabled=false
Payabl__Sandbox=true
Payabl__MerchantId=
Payabl__Secret=
Payabl__PublicOrigin=
```

With `Enabled=false`, the private admin status endpoint says unconfigured and checkout requests refuse to contact the gateway. If enabled, `Sandbox` must remain `true`, `PublicOrigin` must be the separate OS preview's bare HTTPS origin, and real merchant sandbox credentials must come from a secret store. Outbound API calls are **hardcoded to sandbox**, and `start_url` can only redirect to approved sandbox hosts. Never place test or real merchant credentials in Git, UI state, logs or mobile binaries. The webhook path is generated per request and contains an unpredictable nonce; do not log it.

The isolated OS database must have the finance migration applied with an owner/migration role, not the web or mobile runtime user. After inspecting the migration and confirming this is the separate preview database, explicitly apply `ops/postgres/payabl-finance-grants.sql` as the DB migration owner, e.g.:

```bash
psql "$REWRITE_MIGRATION_DATABASE_URL" -v ON_ERROR_STOP=1 \
  -v admin_auth_role=lefteria_admin_auth -v app_role=lefteria_app \
  -f ops/postgres/payabl-finance-grants.sql
```

Adapt role identifiers to the actual dedicated identities. `psql` is intended for isolated preview only: it refuses default PostgreSQL system DBs and rejects cross-role grants and direct finance table privileges. Schema rollback refuses to destroy recorded payment requests; use reviewed forward recovery. Existing runtime DB bootstraps must not receive blanket `finance` table rights. There is **no payment execution** until the migration/grant boundary is reviewed and an approved sandbox merchant is configured.

## Verification / release gates

Run after generating updated OpenAPI and TypeScript contracts:

```bash
dotnet restore backend/tests/LefteriaFc.Identity.IntegrationTests --locked-mode
dotnet run --project backend/tests/LefteriaFc.Identity.IntegrationTests --configuration Release -- --payabl-adapter-policy
npm test --prefix frontend
cd frontend && npm run check && npm run build
```

The contract generator requires a working pinned .NET 10 SDK and Node 24 environment (see root README). The new API contracts have been regenerated in this branch: `contracts/openapi.json` and `frontend/src/lib/api/schema.d.ts`. CI must verify they remain in sync. The offline adapter check uses an in-memory fake HTTP handler; **do not call payabl. from CI**.

Before a live rollout: written merchant approval and contract matrix for payment types, sandbox webhook signature/amount/order verification tests, event replay and idempotency tests in a disposable PostgreSQL DB, concurrency/ownership authorization tests, permissions-boundary audit, TLS callback ingress validation, monitoring, reconciliation controls, approved privacy terms and accounting policy. Integration UI must stay sandbox-labeled until every gate passes.

## Financial safeguards

- Cents stored as integer; no client-reported amount becomes a verified order settlement.
- A browser return, a created checkout URL, or a provider `init` response must never mark an invoice paid.
- Only signed capture events delivered to a unique callback URL and matching the exact OS order ID may mark a test request paid. Payabl capture transaction IDs can differ from initialization transaction IDs. Order IDs are not included in Payabl's simplified signature, so the nonce binding and signed transaction fields must both be verified. Enablement remains sandbox-only pending merchant reconciliation validation. Other events require reconciliation and cannot silently downgrade a paid item.
- Webhook events are idempotent by request, transaction, event type, error and timestamp. A duplicate callback has no second financial side effect.
- The sandbox screen requests only synthetic test payer details; no raw card number, expiry, CVV or employee payout card details.
- Employee payouts should be a separately privileged service with two-person approval, dedicated business payout permissions, verified employee beneficiaries, per-recipient and batch limits, signed request storage, irreversible-transfer warnings and an immutable audit ledger. Never derive payout authority from `finance:write` alone.
- Without payabl.'s merchant contract confirmation, do not assume CFT may be used for payroll salaries. Other regulated banking and payroll providers may be needed.
