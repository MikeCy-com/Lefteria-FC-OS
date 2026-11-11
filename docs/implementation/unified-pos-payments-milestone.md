# Unified POS and payments milestone

**Scope:** Lefteria FC OS, *not* the legacy production repository. Status as of 2026-11-10: **IN PROGRESS / NO RELEASE APPROVAL**.

This is **one milestone** encompassing POS cash/card sales, Academy fee payments,
parent app/website payments, Payabl, receipts, returns, refunds, VAT, financial
posting and reconciliation. A passing POS UI alone is not completion.

## Milestone definition of done — all four phases required

| Phase | Required release evidence | Current state |
|---|---|---|
| 1. Payabl full sandbox lifecycle | Real sandbox payment intent, mandatory provider recheck and merchant/order/amount/currency binding, signed notifications, idempotent capture, pending/failure/chargeback handling, refund request/provider confirmation, replay/duplicate/out-of-order tests; secure credentials | **BLOCKED**: signature helper and private pending inbox exist; neither provider recheck nor capture/refund execution is connected |
| 2. Operational POS | Admin provisions location/register/operator/stock with audited grants; cash/card checkout and change; server VAT line rules; reserved vs consumed/released stock; printable legally reviewed receipts; returns, credit notes, shift closings and till discrepancies | **BLOCKED**: read/admin cashier/stock/receipt/return foundation on draft PR; VAT policies default disabled; cashier checkout and refunds not validated |
| 3. App, website & finance integration | Web shop orders, authorized parent/child allocation, only backend-issued Academy months, payment links, optional consent-based recurring mandates, downloadable invoices, POS gross/net/refunds/fees, unmatched suspense, no double-paid entries | **BLOCKED**: POS to finance outbox stages receipted sales and verified refunds, but no consumer may yet mark finance journal or parent balance paid |
| 4. Testing & release control | Exact-SHA CI jobs actually execute and pass (.NET, SvelteKit, PostgreSQL, migrations, Docker, contract/sandbox); concurrency and security/negative tests, VAT/legal sign-off, reconciliation samples, rollback/recovery, monitoring and release approval | **BLOCKED**: current GitHub Actions jobs fail before execution; no actual PostgreSQL lifecycle sign-off |

**All phases are conjunctive**. No one phase can be marked complete based on
source-level checks or an unexecuted GitHub Actions run.

## Transactional invariants for all channels

1. The user-facing click or redirect is never proof of receipt of funds. A verified
   provider capture, or explicitly authenticated and counted cashier cash
   operation, is mandatory before any finance paid allocation or receipt.
2. Every external transaction uses a unique merchant/organization/order/payment
   tuple and idempotency key. Handle duplicate notifications, reordering, delayed
   settlement, partial capture, provider outages and reconciliation exceptions.
3. Sale inventory and captured money never silently diverge. The same database
   transaction must durably record receipt, stock consumption and posting to
   an immutable **pending** finance event; provider-side money transfer itself
   remains externally reconciled.
4. POS merchandise payments are **not** Academy training fee payments.
   Finance may not infer a child or parent from a sale email, surname or
   organization alone. Fees require a validated existing issued charge/child
   relationship and only configured issued months.
5. VAT treatment must be approved for each sold variant and effective date.
   No global assumption that a 19%, reduced, zero or exempt treatment applies.
   The existing exempt-only checkout policy is disabled until fiscal review.
6. A refund requires original verified payment, bounded sale-line quantities,
   a separately approved return, gateway/cash confirmation, correct stock
   decision, credit note and an idempotent negative finance posting.
   A pending refund request is not money refunded.
7. Role/organization/cashier assignment is reverified on each write and
   sensitive read. Parent/coach/fan roles cannot access cashier or private
   payment evidence, and the original production database remains untouched.
8. Journal posting is idempotent by source event ID and reversible by
   separate immutable correction; never mark a source event consumed before
   the finance journal acknowledges it.

## Implemented cross-domain staging

`20261110164000_PosFinancePostingOutbox` is a **private, append-only** outbox:
after a reconciled paid receipt is inserted, or after an original-payment refund
is independently verified, the source transaction queues a unique event tied
to the exact organization, sale, source document/refund, integer EUR amount and
evidence hash. PostgreSQL rolls back the event if the originating transaction
rolls back. Neither a pending Payabl inbox event nor a pending refund request
can trigger a finance posting. There is deliberately no consumer or
general-ledger/Academy balance mutation: the original finance contracts,
account mappings, fees and charge identity rules have not yet been verified.

Do not add a consumer that blindly marks every Academy parent balance paid.
Do not backfill production. Plan a separate journal reconciler with explicit
idempotent finance transaction identity, signed service-to-service access,
account mapping, fees/tax allocation, negative/refund events, dead-letter
reprocessing and parity tests.

## Verification and release constraints

- Draft development work: [PR #153](https://github.com/MikeCy-com/Lefteria-FC-OS/pull/153).
- Do not merge to OS `main` while any required CI fails or is blocked.
- Never deploy to production; never write to `MikeCy-com/Lefteria-FC`.
- Run disposable PostgreSQL migrations and negative/concurrency verification
  before granting any POS operational functions to the runtime role.
- Real Payabl sandbox credentials, merchant contract and legal tax/fiscal approval
  are integration prerequisites; do not invent them, or embed secrets in source.
- Verify printable invoice/receipt parity against the original approved
  Lefteria FC templates, including cash/card, refund and partial payment
  document states.

## 2026-11-10 implementation slice — finance handoff, VAT arithmetic and sandbox refund transport

1. POS-to-finance integration now has an immutable transaction-scoped `pos.finance_posting_outbox`, a manager-only `pos.read_admin_finance_handoff` function and `GET /api/admin/pos/cashier/finance-handoff` requiring both POS and finance permission. The POS administration page displays the queued receipt/refund totals with an explicit **not yet posted to general ledger** warning. No Academy parent balance is altered.
2. `PosVatArithmetic.FromGross` calculates VAT-inclusive net/tax/gross using **integer EUR cents** and an explicitly effective, enabled and approved rate code/revision. It refuses absent, expired, disabled, mismatched or unknown VAT treatments. Zero-rated and exempt remain distinct classifications. **It is not yet wired to every checkout line or approved for production fiscal calculations.**
3. `PayablSandboxRefundClient` creates a form-encoded **sandbox-only** refund request with payabl.'s documented ordinal key-sorted SHA-1 request signature (Latin-1), EUR currency, captured transaction ID and exact 2-decimal amount. It has a fixed HTTPS sandbox URL, redirects/cookies disabled, strict bounds, and validates the response's original transaction/amount/currency. It always returns a response marked as **requiring independent confirmation**. It does not automatically call from any public/admin route or update refund status. A dedicated audited worker, merchant credential storage, sandbox tests and provider callback reconciliation remain required.
4. Structural checks added for refund signature/identity/redirect handling, inclusive VAT arithmetic, finance posting idempotency, private PostgreSQL functions, and the manager-only finance panel. No .NET/PostgreSQL/SvelteKit CI verification is claimed.

These increments do **not** close phases 1–4. The unified P0 release gate remains blocked; do not grant POS/payment runtime permissions, merge or deploy.