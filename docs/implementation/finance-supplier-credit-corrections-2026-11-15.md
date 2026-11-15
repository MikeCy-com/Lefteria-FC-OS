# Supplier credit notes and payable corrections — 2026-11-15

## Purpose

Supplier invoices cannot be silently updated or deleted after recording. Instead, this continuation of draft PR #351 supports **append-only supplier-issued credit notes**. Every credit note names the original supplier invoice, has a distinct supplier reference, date, amount, documented reason and a requester. The supplier's claim is **pending** until a different, independently authorized Super Admin approves or rejects it; neither role can approve their own correction.

This is a **manual accounts-payable correction**. No refund, transfer, cash receipt, bank statement match, VAT deduction, Payabl call, double-entry journal, payroll movement or legally binding tax treatment is implied. Every JSON position explicitly signals `bank_refund_recorded:false`, `bank_verified:false` where applicable, and `is_general_ledger:false`.

## Invariants and accounting semantics

- Gross invoice amount is immutable and never overwritten.
- Unreviewed credit notes **do not** reduce outstanding liabilities.
- Approved credits lower net payable by exactly their gross EUR amount. Rejected credits have no effect.
- `net_payable = original_bill_gross − independently_approved_credits`.
- `remaining_payable = net_payable − independently_approved_manual_payment_evidence`.
- **At no time may manually approved payments plus approved credit notes exceed the invoice gross**. Before supplier-credit approval, the original bill row is locked and all approved credits/payments are counted. A new trigger protects the **existing** supplier-payment approval function against credits approved in other transactions, with the same original-bill row lock.
- Both credit request and final decision are immutable, auditable append-only records. Duplicate supplier credit references for the same bill and conflicting client request replay are rejected; same-client retry returns its true current review status.
- All money is EUR integer cents. No negative amounts are permitted.

## Authorization and scope

- First Team (`lefteria_fc`) and Academy (`lefteria_fc_academy`) are separate owner ledgers. Every write and read checks account status, active locked admin session, organization grant and role **inside PostgreSQL**.
- Accountants can submit and read credit requests only for explicitly assigned organization(s). They cannot approve or reject them.
- The second reviewer must be a **different Super Admin** with the same organization authorization, including when the first requester was a Super Admin.
- Academy city/location attribution is inherited from the **immutable original bill**. There is no cross-team or cross-site reassignment of supplier invoice records.
- Academy location/city managers have no detailed vendor or credit-note access. Their previous site-scoped gross supplier-obligation summary remains gross and cannot be used to infer confirmed bank spending.
- The restricted admin database runtime receives EXECUTE only for the three new public finance procedures, not direct table privileges or private trigger functions; mobile runtime has none.

## Implementation

- `20261115266000_FinanceSupplierCreditNotes`: new request/decision tables, immutable triggers, payment approval ceiling trigger, append-only authorizations, review functions, and `finance.read_admin_supplier_payable_position`.
- The payables reader **composes** the previous immutable gross invoice / manual-payment register. It returns approved credits, pending credits, net obligations and remaining balance alongside invoice records, payment evidence and up to 200 correction records. It does not destroy or alter historical source data.
- `POST /api/admin/finance/supplier-credits?organization_id=...`: idempotent supplier credit request.
- `POST /api/admin/finance/supplier-credits/{requestId}/review?organization_id=...`: independent Super Admin approval/rejection.
- Existing `GET /api/admin/finance/supplier-payments` now reads the corrected position, including raw gross obligations, approved credits, net liabilities, approved manual payment evidence, and pending unmatched credit claims.
- Separate Svelte UI lets the accountant submit a supplier credit, inspect history, and permits a different Super Admin to review it. The UI never calls credit notes a bank refund or tax deduction.
- `ops/postgres/finance-supplier-credits-grants.sql`, `finance-supplier-credits-checks.sql`, strict SQL EXECUTE allowlist and Svelte contract tests are CI wired.

## Go-live requirements and exclusions

1. Independent accountant/security review, legal/tax suitability in Cyprus, and multi-location permissions.
2. Real credit-note evidence attachments with secure custody, verified supplier identity, bank settlement reconciliation and a correction/reversal method for **incorrect human approvals**; this slice deliberately makes an approved decision immutable, with no automatic unwind.
3. Monthly period close, document retention, VAT classification, complete supplier master, AP aging, general ledger and bank statement imports.
4. Production upgrade only after approved isolated staging migrations and safe forward-recovery/backups, with migration verification against pre-existing club data.
5. PR remains draft/unmerged; **no live Hostinger production changes, no real bank/provider/payment access**.

The earlier `finance.read_admin_supplier_payment_register` remains unchanged; new reads use a composed corrected position. The Down migration removes new append-only credit structures, payment ceiling trigger and new reader before older migration rollback, leaving historical supplier invoices and payment evidence untouched.
