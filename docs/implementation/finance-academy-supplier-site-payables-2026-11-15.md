# Academy supplier payable position by authorized city/location
2026-11-15 · draft finance slice for Lefteria FC OS

## Problem and outcome

An Academy location manager must **never see unrelated city/location finance**, but needs a truthful view of supplier obligations, approved credit notes and approved *manual* payment evidence for their assigned site. The previous site report showed **gross invoices only**. This slice adds a **new, read-only, site-scoped payable position**, preserving the old endpoint and historic invoices.

The implementation has **no bank movement**, no Payabl action, no cash confirmation, no actual general ledger, no VAT calculation and no vendor details visible to managers. A manually approved payment record means an independent Super Admin reviewed an off-system reference; it is **not** bank-verified.

## New interface and calculations

- `GET /api/admin/football/territories/finance/supplier-payables?organization_id=lefteria_fc_academy&month=YYYY-MM`.
- API requires authenticated web admin, exact Academy owner, and existing football territory authorization; SQL additionally checks active session, organization, city/location and assignment through `club.authorize_admin_football` / `club.admin_football_allowed`.
- PostgreSQL SECURITY DEFINER reader `finance.read_admin_supplier_site_payables` returns a **list of allowed city/location IDs only**, with aggregate integer EUR cents for each:
  - `bill_count` and `gross_amount_cents`: all supplier invoices in that bill issue month and authorized site.
  - `approved_credit_cents`, `pending_credit_cents`: independently approved credit notes versus credits awaiting review.
  - `net_payables_cents = gross_amount_cents - approved_credit_cents`.
  - `approved_manual_payment_cents`, `pending_payment_evidence_cents`: accepted manual evidence versus evidence awaiting review.
  - `remaining_cents = net_payables_cents - approved_manual_payment_cents`.
- Pending or rejected credits/payments **never reduce** net payable or remaining amount. **Pending or rejected reversal requests retain the original approved payment/credit effect; independently approved reversals stop counting that old approval.**
- All credits/payments inherit **the original bill's immutable Academy city/location**, never a mutable staff assignment or vendor location.
- Time period is expressly **supplier invoice issue month**. Evidence approved in another month is still included in the corresponding invoice month; this is **not a cash flow month**, and must not be merged blindly with bank receipts, card settlements or general charges.
- `bank_verified:false`, `is_cash_payment:false`, `posted_to_general_ledger:false` explicitly prevent false accounting classifications.

## Scope and privacy

- Super Admin can see authorized Academy location aggregates, not First Team data in this Academy report.
- City managers see their assigned city locations; location managers see only the sites explicitly authorized under their football territorial grants.
- A manager with one site may **not even discover UUIDs** of another site in the same city. Site report returns **no supplier names, invoice references, credit-note references, payment references, document URLs, player data, requester identities, timestamps, or individual transaction IDs**.
- The UI offers an additional city total **only when two or more locations are returned to that actor and belong to the selected city**. It is not a city-wide aggregate unless the actor is actually authorized for all sites in that city.
- The read SQL is owner locked, PUBLIC EXECUTE revoked; the restricted web-admin DB role receives **only EXECUTE** on the new function and no direct finance tables. Mobile DB role is denied.

## Implementation and verification

- Migration `20261115269000_FinanceSupplierSitePayablesWithReversals` creates one read-only SECURITY DEFINER SQL routine.
- Existing `FinanceSupplierBillEndpoints` adds the new manager-only route while leaving `/supplier-bills` gross report backward compatible.
- `AcademySupplierSiteTotals.svelte` switches to new endpoint, validates amount invariants and strictly whitelists site aggregate keys. Displays six accessible cards with a clear manually-approved/evidence caveat.
- `ops/postgres/finance-supplier-site-payables-grants.sql` audits function ownership, hardened search path and minimal SQL privileges.
- `ops/postgres/finance-supplier-site-payables-checks.sql` creates synthetic bills at two sites within one city, another city, and a First Team-only bill. It checks approved/pending/rejected payment and credit amounts, same-city site isolation, First Team isolation, no vendor leakage, month validation and revocation. Everything rolls back.
- `frontend/tests/finance-supplier-site-payables.test.mjs` verifies Svelte compilation, endpoint wiring, explicit security contracts and SQL synthetic-test coverage.
- All SQL checks run in disposable PostgreSQL CI; no production credentials or real finance records involved.

## Next go-live gates

1. Review branch integration against the latest `main` without replacing other teams' work.
2. Independent authorization and finance review, including accountants' month close and supplier evidence retention policies.
3. Add human-approved **audit-safe reversal events** for mistaken payment/credit approvals, with concurrency control and no destructive mutations.
4. Add verified bank reconciliation, accounting periods, proper chart of accounts and balanced double-entry GL journals, VAT review, and migration/backup approvals.

**Draft PR only. No live Hostinger deployment or real payment processing.**

## Reversal integration and safe dependency order (2026-11-21)

This report is built **on top of draft PR #398** (`20261115268000_FinanceSupplierApprovalReversals`) as a stacked PR. The Academy site migration is intentionally numbered **`20261115269000`**, after the append-only reversal approval tables. Do not merge/run the old standalone draft [PR #379](https://github.com/MikeCy-com/Lefteria-FC-OS/pull/379) independently: that version computes site balances without excluding later approved reversals.

The SQL report calculates **effective approved manual amounts** using `NOT EXISTS` on a final independently approved reversal of the *same target kind and target ID*. Reversal requests that are pending or rejected **do not** change original sums. The report sets `approval_reversal_adjusted:true` and the client rejects a response lacking that contract. The enhanced synthetic SQL fixture exercises two locations in the same city, another city and a First Team invoice; it tests pending, rejected and independently approved reversals for both credit and payment records, and asserts other sites remain invisible and unchanged. No identities, vendor details or reversal reasons are returned in the manager report.

A remaining technical limit is that these are **current effective positions for invoices issued in the selected month**, not month-end historical snapshots or a statutory payable ledger. A correction made months later may change the displayed position of the original invoice month; period close, as-of reporting and adjustment journals must be implemented before accountant sign-off.


## Integration reconciliation — November 21, 2026

This source branch is now based on `main` after four-eyes reversal PR #398 was merged. Its location totals are explicitly reversal-aware and replace, rather than coexist with, the earlier standalone location-payables PR #379. A location manager's figures must reflect only independently approved, non-reversed manual evidence for their currently authorized Academy locations; no bank settlement or GL posting is implied. Before an operational release, verify the combined implementation on an isolated staging database and retain human financial approval.
