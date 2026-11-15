# Supplier payment evidence and independent approval — 2026-11-15

## What was implemented

This continuation of draft PR #351 adds a two-stage **manual payment-evidence register**, linked to immutable supplier bills from the previous finance slice. It is **not bank reconciliation** and it never invokes Payabl, a bank, SMS, the POS or a live payment processor.

1. Accountant (or authorized Super Admin) records a payment-evidence request for one existing supplier invoice: requested amount, past/present date, declared payment method and off-system evidence reference. It stays **pending** and does not reduce the reported outstanding balance.
2. A **different** organization-authorized Super Admin must independently check the supporting evidence and either approve the manual record or reject it, documenting an immutable reason. The requester cannot self-approve, even when the requester is also a Super Admin.
3. Approval is serialized on the original invoice row and cannot exceed its gross amount, including earlier partial approved records. Rejected requests cannot consume the balance; conflicting second decisions are denied.
4. The finance UI shows the invoiced gross amount, manually approved amounts, remaining amounts and **pending (unapproved) evidence** separately. None is called provider-verified or posted to the general ledger.

The system **cannot know** if a bank transfer or cash deposit truly occurred from a typed reference alone. A human's manual approval means evidence was reviewed off-system; `bank_verified:false` and `is_general_ledger:false` are explicit in the read API. These records are not statutory accounting data until a bank/receipt reconciliation and balanced journals are designed.

## Data integrity and permission boundaries

- `20261115265000_FinanceSupplierPaymentReview` migration creates `finance.supplier_payment_requests` and `finance.supplier_payment_decisions`, with immutable UPDATE/DELETE-denial triggers, constraints, per-organization idempotency and audit security events.
- Security-definer functions lock the real admin session, check active account, organization grant and role, and never give the restricted admin or mobile runtime SQL access to the raw tables.
- Accountants receive `finance:read` and `finance:write` within their explicitly assigned First Team or Academy organization, but **cannot approve** manual settlement evidence.
- Super Admin can approve only **another user's** evidence; a second Super Admin can review evidence created by the first Super Admin.
- Managers and other roles cannot read supplier names, payment references or detailed approval records. The existing Academy location manager aggregate from the earlier slice remains geographically restricted and does not count unverified evidence as money spent.
- First Team and Academy bills are owned by different organizations. Academy payments inherit the **unchangeable original supplier-bill city/location snapshot**.
- Concurrency: each reviewer locks the permanent bill row with `FOR UPDATE` before checking sum of prior approvals and inserting a final immutable decision; two simultaneous approvals cannot overpay the same bill.
- Replay of a previously submitted request gives its **actual current review status**; it cannot misleadingly return `pending` after approval.

## APIs and tests

- `GET /api/admin/finance/supplier-payments?organization_id=...&month=YYYY-MM` — bill issue-month aggregate and capped evidence list (100 bills, 200 requests).
- `POST /api/admin/finance/supplier-payments?organization_id=...` — idempotent, owner-checked pending evidence request.
- `POST /api/admin/finance/supplier-payments/{requestId}/review?organization_id=...` — independent Super Admin review, with audit note.

`ops/postgres/finance-supplier-payments-grants.sql` confirms the restricted role and SECURITY DEFINER owner/search path before granting only specific functions. `finance-supplier-payments-checks.sql` uses synthetic users/bills/sites in `BEGIN/ROLLBACK` to verify 4-eyes, partial approval, amount caps, role boundaries, cross-organization isolation, immutable rows, revocation and audit events. Existing strict web-admin SQL grant allowlist and CI integration are extended. Svelte contract tests compile the new interface.

## Remaining high-priority accounting gaps

- Bank statement/import + actual bank verification, duplicate proof matching, reconciled cash drawer deposits and settlements. **Do not represent a typed bank reference as bank-verified.**
- Evidence file custody, antivirus/quarantine, permissioned downloads, tamper-evident document storage, payment reversal/credit note workflow, period locks and supplier master records.
- True double-entry general ledger, chart of accounts, journals with balanced debit/credit, VAT/tax rules reviewed by a Cyprus-qualified accountant, cash book, payroll, budgeting, reconciled liabilities and financial statements.
- Safe migration of older club records; preserving live invoices and Payabl state; per-location accounting posting and management reporting without leaking another city/site.
- Human approval of the finance/security design before any live migration. **No production host or real payment provider was touched.**

## Rollback and deployment boundaries

Staging-only migration with an approved independent rollback/forward-recovery plan. The Down migration removes only new payment evidence/review tables and functions; existing supplier bills and their original invoices are left unchanged. Unreviewed evidence must never be silently counted as a settled supplier liability.
