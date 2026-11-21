# Supplier approval reversal: immutable double-review corrections
2026-11-15 · Lefteria FC OS · staging-only implementation

## Accounting problem

An accepted **manual** payment-evidence entry or supplier credit can later prove invalid or duplicated. Existing immutable records must never be rewritten. This implementation creates a separate, independently approved **reversal of the manual liability impact** while retaining the original supplier invoice, payment evidence / credit note, initial reviewer decision, correction request and final reviewer decision.

**It does not return money, reverse a card charge, contact a bank, void an official VAT document, or produce a statutory double-entry journal.** Manual approvals explicitly preserve `bank_verified:false`, `bank_refund_recorded:false` and `posted_to_general_ledger:false`.

## Workflow

1. An Accountant or Super Admin, strictly assigned to the invoice's First Team or Academy organization, submits an idempotent reversal request against an **already approved** manual payment or credit. A 12–180 character correction reason is mandatory.
2. The request is pending and has **zero** effect on liability. An already rejected original approval cannot be reversed.
3. A **different, authorized Super Admin** must check the supporting evidence and approve or reject the reversal. That reviewer must differ from both **the reversal requester** and **the Super Admin who approved the original entry**. A final decision is immutable, repeatable only with exactly identical payload by the same reviewer.
4. Approving the reversal removes the original approval's liability effect, without deleting the original record. Rejected reversal requests have no effect. A target may have multiple rejected correction requests but can have **only one approved reversal**; all approvals serialize on the immutable invoice row.
5. Replaying the same idempotency key returns the current decision, not a stale pending response. Conflicting payloads and attempts to reverse an already-reversed approval are denied.

## Financial totals

All EUR amounts are integer cents, with original invoice owner and site inherited from the immutable supplier bill. For each invoice:

- `approved_payment_evidence_effective = sum(approved payment evidence without an approved reversal)`.
- `approved_credit_effective = sum(approved supplier credits without an approved reversal)`.
- `net_payable = invoice_gross − approved_credit_effective`.
- `remaining = net_payable − approved_payment_evidence_effective`.
- **Pending or rejected** reversals leave the original approval effective.
- Database payment and credit approval checks, the payment-decision insertion ceiling trigger, the original payment reader and the composed credit/payable reader all use effective amounts. This is critical: reversing an approval must not permanently consume the bill's financial ceiling, and replacement entries must never overpay.
- Monthly reporting uses the **original supplier bill issue month**. This is not a statement-period cash-flow report.

The read response includes `reversed_manual` flags on individual approvals and a capped `reversals` audit history. The accounting UI marks reversed approvals distinctly; it never calls a correction a bank refund or cash movement.

## API and storage

- `POST /api/admin/finance/supplier-reversals?organization_id=...` creates an idempotent, pending request. Body: `client_request_id`, `target_kind` (`payment` or `credit`), `target_id`, `reason`.
- `POST /api/admin/finance/supplier-reversals/{reversalId}/review?organization_id=...` applies documented Super Admin approval or rejection; body: `approve`, `review_note`.
- Existing `GET /api/admin/finance/supplier-payments?organization_id=...&month=...` now includes corrected position and reversal history.
- Migration `20261115268000_FinanceSupplierApprovalReversals` adds request and decision tables with immutable UPDATE/DELETE-denial triggers, owner/client-id uniqueness, active admin authorization, audit events and five updated existing database functions. `Down` restores the prior five functions *before* removing new tables; historical supplier invoice and payment/credit records are untouched.
- `finance-supplier-reversal-grants.sql` audits role hardening, function ownership, locked search path and denies raw table access. The mobile DB role has no access. Only narrowly scoped SECURITY DEFINER functions are granted to the admin runtime.
- `finance-supplier-reversal-checks.sql` executes synthetic accountant/Super Admin/site-manager/First Team users, bill and evidence in `BEGIN; ... ROLLBACK;`, testing original-reviewer denial, immutable rows, cross-org isolation, revocation, replay, pending/rejected/approved decisions and correct replacement balances. Frontend contract tests compile all panels.

## Site-specific caution and dependency

[Draft PR #379](https://github.com/MikeCy-com/Lefteria-FC-OS/pull/379) separately adds Academy location manager payable summaries. **It currently computes credits and payments without approved reversal exclusions.** The two PRs **must not be rolled out together as finance truth** until site aggregates are updated to use effective, reversal-aware sums and tested against same-city location scope. This is a tracked integration gate, not a claim of ready-to-use manager financial statements.

## Required next actions

- Review this migration's append-only semantics, evidence quality, permissions and concurrency; test in isolated staging with no production finance/customer records.
- Connect provider/bank reconciled settlement records separately; require actual external statement proof before calling a payment bank-verified.
- Add evidence custody and data retention, chart of accounts, double-entry journals, posting reversal journal entries, period close, VAT handling reviewed by a Cyprus-qualified accountant, and accounting migration backups.
- Coordinate PR #379 and this PR with current `main` and independent finance/security review before merge. No live Hostinger deployment or Payabl production actions.
