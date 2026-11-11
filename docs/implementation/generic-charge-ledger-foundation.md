# Original Lefteria-FC charge ledger — private foundation

**State: DRAFT / NO DEPLOY / NO LIVE MONEY MOVEMENT.**

**Source pinned for semantic parity:** `MikeCy-com/Lefteria-FC`,
`backend/models.py`, source `main` commit
`91eafcc9efd01ad177b4f13a869cd5af7041e5b2`. Original
`ChargeType`: `training,event,grassroots,registration,equipment,tournament,
transport,other`. Original `ChargeStatus`: `pending,paid,overdue,cancelled`.
Original supports `ChargeTemplate`, `ChargeCreate`, explicit monthly range,
manual payment `paid_amount` and the methods `cash,bank,card,online,other`.

This is a **stacked branch on `feature/payabl-payments-foundation`**, not a
merge into OS main. The Payabl branch remains draft and depends on a provider
merchant sandbox plus currently unavailable runner-based CI.

## Implemented, private schema only

`20261109130000_GeneralChargesPrivateLedger` adds three private tables and
a private, ungranted balance view:

- `finance.general_charge_templates`: original eight charge categories,
  EUR-only integer-cent defaults, description and manual active flags.
  Creating a template **does not issue any months**.
- `finance.general_charge_issues`: an explicitly issued single child/charge
  in a specific organization, with text/season, source legacy charge ID,
  validated `YYYY-MM` period key, optional due date and original source
  SHA-256 digest. Child+org are enforced by composite FK. Original amount,
  currency and description become immutable when issued; cancelled invoices
  cannot be deleted. A legacy invoice without verified guardian evidence
  remains unlinked and **cannot appear in the parent app**.
- `finance.general_charge_parent_verifications`: separate immutable,
  organization-constrained guardian evidence links; a historical charge
  can be verified later without modifying its original billing snapshot.
  The balance projection exposes a guardian ID only after this evidence
  exists. A future parent reader must **also** recheck the live canonical
  guardian/child relationship when the PDF or charge is accessed.
- `finance.general_charge_events`: evidence-bound, uniquely idempotent
  partial/full payments with one exact reversal per payment; serializes on
  charge row and ensures (0 \leq \sum events \leq original cents\).
  Accepted type captions include cash, bank, card, online and other, but
  no method becomes payable without a separately verified collection adapter.
  Neither financial events nor original amount can be edited or deleted.
- `finance.general_charge_balances`: source-compatible display statuses
  `pending/paid/overdue/cancelled`, with derived paid and due EUR cents.
  Overdue comparisons are by local Cyprus date. It does not mark card
  redirects as paid.
- Default-deny PostgreSQL RLS plus explicit PUBLIC revocations; there is
  **no runtime EXECUTE/SELECT/INSERT/UPDATE grant** to app, website,
  marketing or web-admin roles. Operators must never use database migration
  credentials to create manual receipts in a live system.

## Test evidence planned

`ops/postgres/general-charge-private-ledger-checks.sql` rolls back a
synthetic, isolated child and charge fixture. It exercises all key source
statuses, partial and full collection, duplicate idempotency, overpayment
refusal, one exact reversal, no duplicate refund, immutable event and amount,
single allowed unpaid cancellation, and verifies that templates do not create
12 (or any) months. It checks admin/mobile raw table denial.
`frontend/tests/general-charge-ledger-foundation.test.mjs` provides
source contract assertions. Both are attached to the standard rewrite CI.
**These do not yet have a successful GitHub Actions runner execution.**

## Work remaining to make original charge flows functional

1. Map actual legacy MongoDB charge/template/payment documents into immutable
   source IDs and reconciled amounts in cents, preserving split payments,
   source currency, old status, refunds and receipt references. No silent
   conversions from rounded Python float and no invented phone/guardian.
2. Resolve payer/child authorization against current canonical guardian links,
   audit historical payer evidence before adding a one-time verified guardian
   association, and
   preserve printed legacy invoice identity and fiscal numbering.
3. Add manager-only, role-scoped **manual issue API** and UI for exact selected
   child, item and due date, with validation, idempotency and business audit.
   Do not issue 12 months automatically.
4. Add verified methods for **cash** (manager-attested receipt),
   **card** (Payabl signed & reconciled capture), **bank** (bank statement
   verified), `online` or `other` (approved evidence) and exact reversal.
   The current table by itself makes no external payment, confirmation, SMS
   or card vault operation.
5. Expose safe parent bills and administrator reports, complete source-matched
   individual/multi-item PDF rendering including partial/credit/refund,
   stamp verified tax/fiscal receipt number and Cyprus accounting fields.
6. Green .NET 10, PostgreSQL isolated migration/rollback, Svelte check/tests,
   generated API schema and Docker proof at the exact merged candidate SHA.

This foundation alone **does not pass the finance go-live gate**, does not
modify the original repository, and does not deploy to Hostinger or live.

## Integration with merged Payabl foundation (2026-11-10)

PR #143 has now been merged into OS `main`. This ledger PR targets `main` directly and is **schema-only**: it adds no card-capture, cash-collection, refund, subscription or public/mobile read endpoints. The separate general-charge SQL fixture is wired into the existing disposable PostgreSQL CI job, after the Payabl test setup, and must pass with both restricted runtime roles. A successful build is evidence of migration/constraint behavior in disposable CI only; settlement mapping, invoice parity and accounting reconciliation remain separately blocked for release.

The event guard serializes per charge, enforces signed amounts and prevents partial payments from exceeding the issued amount. SQL checks must exercise duplicate idempotency, original-event reversals, immutable issued amounts and evidence-method combinations. **Do not apply the migration to production until the exact-head PR checks and reviewed finance rollout are complete.**
