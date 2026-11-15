# Finance: manual supplier bill obligations (draft implementation, 2026-11-15)

## Accounting purpose / what this is NOT

A private, **manual supplier invoice register**. Each record is a documented *gross obligation* only. Recording it does **not** mean a bank payment occurred; it never calls Payabl/Twilio, records VAT credits, posts to a double-entry general ledger or automatically creates an expense in a legal profit-and-loss statement.

The register complements, but does not replace or add up with, the existing independent revenue sources in the finance overview. No chart of accounts, GL posting, bank reconciliation, expense approvals, supplier attachment custody, credit notes or invoice payments exist in this slice. No production credentials/data and no Hostinger deployment.

## Organizations and geographic authorization

- **Super Admin**: read/write supplier invoice records for assigned organizational scopes, and read authorized Academy location aggregates.
- **Accountant**: explicit organization grant + `finance:read` / `finance:write`. Can read/record vendor bills only for granted First Team or Academy, never another owner. No API access to academy rosters/children from this feature.
- **Academy Manager**: cannot read supplier names, references or invoice descriptions and cannot record vendor bills. The manager's Academy site summary checks organization, session, assigned city/location on every read. City managers can see assigned-city locations; location managers only their assigned locations.
- **First Team**: independent owner ledger. No Academy location selection is allowed.
- **Academy**: requires one active, exact city/location at creation; persisted city/location is a snapshot, not inferred from mutable group assignment. A site must belong to an active Academy city scope.
- **Revoked users**: role, active account, organization grant and token checked inside PostgreSQL on each read/write. Browser checks are supplementary only.

## API and schema

- `GET /api/admin/finance/supplier-bills?organization_id=...&month=YYYY-MM`: returns owner-specific month summary, up to 100 recorded bill rows, active site picker.
- `POST /api/admin/finance/supplier-bills?organization_id=...`: idempotent bill submission with a `client_request_id`, `supplier_name`, `invoice_reference`, category, description, bill date, gross EUR cents, and Academy `location_id`.
- `GET /api/admin/football/territories/finance/supplier-bills?organization_id=lefteria_fc_academy&month=YYYY-MM`: safe per-site counts/amounts; no vendor fields.

Migration `20261115264000_FinanceSupplierBillRegister` creates `finance.supplier_bills`, owner-scoped unique vendor/reference, conflict-safe replay and an audit event. Trigger prevents UPDATE/DELETE; missing reversals/credit-note logic must be added before production accounting adoption. PostgreSQL routines are SECURITY DEFINER with a locked `search_path`, PUBLIC execute denied and no raw table access for admin or mobile DB roles.

## Integration verification

`ops/postgres/finance-supplier-bill-grants.sql` validates DB roles/function ownership and applies only minimal function EXECUTE grants. `ops/postgres/finance-supplier-bill-checks.sql` executes synthetic, rolled-back tests for duplicate invoice and client request rejection, replay, owner boundaries, Academy active sites, manager site redaction, token revocation, audit events, and immutable documents. Workflow CI calls both, followed by existing strict web-admin function allowlist test. Frontend contract tests compile the panels.

## Go-live gates and forward recovery

1. Finance/security review the draft PR and confirm acceptable invoice status, accounting standards, correction and audit semantics. Do not use this manual register to report tax liabilities or cash expenses.
2. Run migrations and grant scripts **only in disposable staging**; verify full CI and compare with the legacy invoice/finance workflows.
3. Production migration requires an approved backup and forward recovery plan. Do not drop or overwrite existing invoice/payment data.
4. Before any real financial go-live, implement verified supplier identity, attachment evidence, credits/reversals, approval workflows, posting of balanced journals, bank/cash settlements and reconciliation, VAT/tax handling by qualified accountants, policy for date locking and retention, and safe data migration.
