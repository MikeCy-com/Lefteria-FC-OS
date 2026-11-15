# Club accounting centre: first restricted overview (2026-11-15)

**Status:** proposed isolated OS feature branch; not a QuickBooks replacement and not
production-ready. No deployment, live payments, payroll payout, refund, bank connection,
card storage or live Twilio/Payabl configuration is included.

## Why this vertical slice exists

The UI already recognized the `accountant` role and `finance:read/write`
permissions, but the Academy invoice and training fee PostgreSQL functions
still explicitly allowed only `manager`/`super_admin`. Conversely, the
organization-wide Payabl routes admitted any `manager` with an Academy org
grant, regardless of city/location restrictions. An Academy location manager
must never list every child's organization-wide invoices, contact details or
payment requests.

This branch provides an **actual server + PostgreSQL guarded financial overview**
for `accountant` and `super_admin` on explicitly granted organizations.
For safety, the legacy organization-wide Academy invoice/fee and Payabl API
routes become Super Admin only until a revised accountant-specific,
audit-reviewed write and per-location invoice workflow exists. Scoped location
finance remains under `/admin/territories`. This deliberate lock-down changes
the legacy manager payment workflow; it cannot be re-enabled solely by
removing a UI warning.

## Current protected overview

- `GET /api/admin/finance/overview?organization_id=...&month=YYYY-MM`
  authenticates a live admin session and `finance:read` permission, requires a
  granted organization and restricts role to accountant or super admin.
- An independent `SECURITY DEFINER` PostgreSQL function locks the session
  and exact organization grant and rechecks active account, role and scope.
  The caller gets approved aggregate amounts and counts **without player,
  guardian, supplier, telephone or contact fields**. No raw table privileges.
- Distinct First Team vs Academy selection, with no mixed owner in one query.
  A manager must use verified city/site finance; they cannot query owner-wide
  reports through the new function.
- Academy monthly training fees (issued, confirmed paid, issued/open and
  review required), separate other charges (issued/collected/outstanding)
  and **sandbox** Payabl request counts. These are not additive because source
  records can overlap, and months reflect different source-specific clocks
  (training fee period vs general charge issue month).
- This is **not** a bank balance, reconciled cash flow, general ledger, income
  statement, statutory tax statement or verified full historical import.
  Refunds and disputed adjustments require independent verified accounting
  mappings before they can appear in formal totals.

## Migration/release gates

1. Build/run the exact PR SHA in disposable CI, including .NET compile,
   SvelteKit check, node tests, and PostgreSQL `finance-accounting-overview-checks.sql`.
2. Apply migration `20261115263000_FinanceAccountingOverview` only to an
   explicitly isolated OS test database using the migration owner, never the
   runtime web database login.
3. Run `ops/postgres/finance-accounting-overview-grants.sql` as DB owner with
   `admin_auth_role` and `app_role`. It checks function owner, secure
   search_path, absence of PUBLIC execute, and absence of direct finance
   table privileges.
4. Exercise a Super Admin granted to both clubs, Academy-only accountant,
   revoked accountant, city manager and location manager; inspect the
   accessible UI and DB projections using synthetic records only.
5. Require independent accountant review of original Lefteria-FC data,
   tax/invoice identity, historical refunds and financial evidence before
   declaring any report official. Never activate real gateways by merge.

## Complete football-club accounting destination

The eventual `ΔΙΑΧΕΙΡΙΣΗ → Οικονομικά` must contain genuine persisted,
auditable workflows rather than duplicated placeholder pages:

| Area | Intended verified behavior |
| --- | --- |
| Chart of accounts | Per-owner EUR accounts for assets, liabilities, equity, revenue and expenses; subsidiary city/site dimensions |
| Customer receivables | Separate First Team and Academy invoices, billable months only, partial payments, credit notes, disputes and archived PDFs |
| Supplier payables | Vendors, dated bills, attachments, approvals, due dates, allocations and protected settlement evidence |
| Expenses | Classified location or First Team spend, expense requests, receipts, tax metadata and dual-control approvals |
| Payroll accounting | Player/coach/staff contract expenses, liabilities and reports; payouts disabled until approved provider/legal verification |
| POS integration | Location-identified, verified receipts, cash shifts, refunds and stock-cost postings; no duplicate revenue |
| Bank/cash reconciliation | Imported verified bank statement rows, exception queue, matched transactions and month-end closing locks |
| Reports | Per owner, Academy city, Academy location and consolidated Super Admin views; P&L, balance sheet, receivables, payables, cash-flow and budgets |
| Governance | Accountant financial role separate from user/roster roles; site managers see only their sites, city managers their city; immutable postings, reversals and review trails |

**Accounting invariant:** A posted double-entry journal must balance in EUR
cents within its owner, use a source/idempotency key and never combine a First
Team source with an Academy account. Unallocated historic Academy charges remain
in review, not assigned to today's location by guessing from current rosters.
Every correction must use a compensating journal, not editing an earlier posting.

**Not implemented in this branch:** chart of accounts, expenses, supplier bills,
payroll, reconciliation, financial statements, VAT/fiscal close, accountant
issuance and payment approval, First Team invoice issuance, location-specific
invoice issue/collection UI, or complete imported legacy transactions.
