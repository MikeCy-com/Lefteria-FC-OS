# POS cashier, payment, receipt and refund ledger — draft release gate

Target: **Lefteria FC OS only**. Original `Lefteria-FC` production and public/mobile payment paths are unchanged.

## Added in this slice

- Private, organization-scoped `pos.locations`, registers, cashier assignments and one-open-shift-per-register records. No public or mobile grants.
- Per-location variant stock with nonnegative/reserved bounds and a version field.
- Idempotent sale identity `(organization_id,request_id)`, immutable-intended line snapshots, server-side exact product/category/variant/name/cost/price checks, quantity constraints and EUR-cent arithmetic.
- Payments carry `cash` or `card`, pending/verified/failed states. Card verification requires provider payment ID and evidence digest; an order intent is not capture. A payment cannot exceed sale total. The eventual API must verify Payabl webhooks and reconcile external settlement **server-side**; never trust browser redirect, callback form values or parent role.
- A sale may transition to `paid` only when verified payment events equal its total and line snapshots equal its subtotal. Receipt insert requires the paid sale, exact register, verified payment total and receipt payload total. Numbering table/uniqueness make a future atomic per-register receipt issuer possible.
- Return quantities are bounded against the original sale line, including earlier pending/approved/refunded returns. Refunds must point to verified original payment on the same sale, with the same payment method, and cannot exceed captured amount or returned-line value. Pending refund confirmation has a guarded transition and requires evidence for card refunds.
- Audit table and immutable financial-history triggers; private default-deny RLS without runtime grants; rollback refuses to drop populated sale, receipt and refund data.
- Structural regression test `frontend/tests/pos-cashier-ledger-foundation.test.mjs`, disposable PostgreSQL script `ops/postgres/pos-cashier-ledger-checks.sql` and CI wiring.

## What is **not** operational yet

**This migration is a secure schema foundation, not a finished POS.** It does NOT enable checkout, authorize a cashier session, deduct inventory, verify a provider webhook, issue official tax/legal receipts, or transfer/refund funds. No runtime role can insert a sale or receipt under the new schema. Building a UI that claims otherwise is prohibited.

The next implementation gates before enabling checkout:

1. Cashier identity / two-factor, explicit register and location assignment, shift open/close and cash reconciliation, including revocation mid-session.
2. Approved product tax/VAT policy, location inventory reconciliation, stock reservations and atomic stock deduction/restocking with retry/idempotency. A browser-supplied tax/discount/total never has authority.
3. Atomic `create_cash_sale` transaction that locks stock, prices from current catalog, validates cashier shift, records counted cash evidence, assigns receipt number under lock, writes fiscal receipt payload and immutable audit, all-or-nothing.
4. Card `pending` sales followed by a separately authenticated, signature-verified Payabl capture/settlement event; failed/unpaid orders issue **no receipt**. Duplicate or out-of-order provider events must be replay-safe. Authorization/redirect is not capture.
5. Approved returns with sale-line quantity limits and separate cash refund counts/card refund provider confirmation, restocking decision, credit note/refund receipt, audit and finance reconciliation.
6. Manager POS UI, operator screen, location stock count, shift report, receipt print/PDF download and source UI parity.
7. Independent security audit; real .NET build, PostgreSQL lifecycle/rollback/negative tests, receipt fiscal/legal review for Cyprus, OpenAPI/client generation, SvelteKit tests, full CI at exact SHA and explicit release sign-off.

**Safety:** Never deploy to live, turn on payment buttons or grant runtime POS writes from this PR until these gates pass. Node source assertions are not runtime/database proof.

## Verified receipt finalization gate (additional implementation)

Migration `20261110152000_PosVerifiedReceiptIssuer` adds a private, revocation-aware SQL function for **already verified**, fully reconciled sales. It locks the exact sale, rejects incomplete/unverified payment, checks current management organization grants, allocates a register-scoped sequence under row lock, snapshots itemized line/amounts and fiscal-policy version, hashes the immutable receipt payload, appends security audit and returns the existing receipt upon replay. A disabled-by-default, owner-only `pos.receipt_policies` row is required; migration creates no enabled policy and provides **no runtime execute grant**.

This is a receipt-issuance building block, **not live legal/fiscal authorization**: tax/VAT treatment, policy approval, receipt PDF/printing, provider settlement and cashier checkout remain unimplemented and must be separately verified. No receipt is issued merely because a browser requested a payment link.

## Cashier register and cash shift slice — 2026-11-10

Added migration `20261110153000_PosCashierShiftOperations`:
- Secure database functions `pos.read_admin_cashier_registers`, `pos.open_admin_cashier_shift` and `pos.close_admin_cashier_shift`, plus a non-granted private authority helper. The caller must be a current management admin with the exact organization grant, an explicitly active cashier assignment, and active register/location. Opening/closing requests use unique UUID idempotency keys and append `pos.transaction_audit`.
- One open shift per register is enforced by the existing unique partial index. Opening cash and counted closing cash are fixed EUR cents, each capped at €1,000,000; a close records expected cash and cash variance. Expected cash equals opening cash + verified cash sales − verified cash refunds for that shift, and refuses unresolved sale/refund transactions.
- Added protected manager routes `GET /api/admin/pos/cashier/registers`, `POST /api/admin/pos/cashier/registers/{registerId}/shifts`, and `POST /api/admin/pos/cashier/shifts/{shiftId}/close`; organization, role, `shop:write` and session are rechecked by HTTP and SQL.
- Added `AdminPosCashierShiftsPanel.svelte`, integrated into the existing `/admin/pos` page, showing only verified assigned registers. It supports opening balance input, deliberate counted close and privacy-safe refresh; no cashier checkout, receipt print, card capture or sales are activated.
- Added narrowly scoped **operator-initiated** `ops/postgres/pos-cashier-shifts-admin-grants.sql`. It must not be run until a reviewed environment is provisioned. Migration itself grants no runtime access. Grant script verifies web-admin role independence, restricted functions and direct table access denial.
- Added source-level security regression tests and extended the isolated PostgreSQL RLS/trigger probe to verify the new private functions and idempotency indexes.

**Caveats:** The first operational slice still requires approved register/location provisioning and cashier assignments, cash/stock control testing, an independent operator identity workflow, signed provider settlement, production receipt tax/legal compliance, and fully audited sale/refund transactions. `shop:write` is necessary but never sufficient without an active register-specific assignment. All operations remain disabled until explicit SQL permissions are applied and CI/integration tests pass. Do not merge to OS main or deploy while unverified.
