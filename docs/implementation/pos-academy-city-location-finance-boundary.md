# Academy POS city, site and manager boundaries — staging milestone

**Status: DEVELOPMENT ONLY — NOT CLEARED FOR LIVE SALES, PAYABL, REFUNDS OR DEPLOYMENT.**

## One canonical geography

The Academy's authoritative city/location directory is `club.football_cities`
and `club.football_locations`. A POS site is a separate operational location
with its own product availability, stock, registers, shifts and transactions.
`pos.academy_football_location_links` binds each POS location to exactly one
canonical active Academy location, with a composite city/location foreign key.
There is no name-matching guess, no reuse of another city's venue or account
and no automatic migration of pre-existing unlinked POS data.

A Super Admin can provision the POS counterpart of **an existing** Academy
location, and only then register multiple terminals for the site. The prior
Academy `/api/admin/pos/cities` and `/locations` create paths now reject
duplicate parallel Academy geography; first-team POS locations remain separate.

## Financial authorization

Each finance request takes the current web-admin session and the precise
organization and POS location. The database resolves that POS location to a
canonical Academy site and re-evaluates active grants, at request time:

| Actor | Site access |
|---|---|
| Super Admin with organization grant | All mapped Academy sites |
| Academy city manager | Mapped sites inside that city only |
| Academy location manager | Only their exact assigned Academy site |
| Cashier assigned to a terminal | Existing shift functions for that terminal, subject to current Academy grants |
| Coach, family, player, fan, unassigned management | Denied |

**Global accounting is separate from site management.** The Academy's
organization-wide Payabl payment requests, training-fee ledger, SMS workflow,
cash postings and invoice PDFs require an active `super_admin` or
`accountant` account with the exact organization grant. The API, navigation
and ten underlying PostgreSQL security-definer functions enforce this, so a
`manager` with a city or location grant cannot open the full Academy ledger.
Accountants may use global finance but do not inherit football territory,
team, POS terminal, user management or other role privileges. A manager's
permitted Academy finance view remains the canonical site-attributed report
under `/admin/territories`, while POS sales are reported in `/admin/pos`.
Existing payment records are not rewritten when roles change.

The independent first-team POS location-to-team manager mapping is not yet
defined. Until then, its new finance site reports are **Super Admin only**,
rather than granting an organization-wide first-team manager access by guess.

The existing **organization-wide POS location/stock management helper** now
requires Super Admin; existing organization-wide POS finance handoff is also
Super Admin only. The register listing and cashier authority recheck Academy
site access, so a stale terminal assignment does not bypass revoked territory
permissions. All protected functions are `SECURITY DEFINER`, fail closed and
remain private to PostgreSQL until the manually approved grants script runs.

## Reporting and history

`GET /api/admin/pos/sites` returns only authorized sites, and the exact
`/api/admin/pos/sites/{locationId}/finance` report returns receipts, verified
cash/card events, verified refunds and exception counts for registers attached
to that one POS location. Paid-but-unreceipted entries are disclosed as a
reconciliation exception, not silently counted as receipted.

`GET /api/admin/pos/sites/{locationId}/sales` returns the 50 latest real
sale/order snapshots at **that one site** with status, actual receipt number
(if one exists), register and amount. Awaiting/partially paid/voided sales are
never presented as completed payments. These are read-only reporting APIs,
not checkout or public webshop fulfillment.

## Terminals and personnel

The Super Admin terminal setup has a reviewable directory of existing sites,
registers, active assigned admin operators and canonical Academy sites.
Register creation initializes a distinct physical terminal and receipt
sequence for one location. Operator assignment adds or revokes an explicit
`pos_cashier.cashier_assignments` row, after checking the subject's current
organization and canonical location/city manager grant.

**Dedicated Cashier credential (development only):** A Super Admin can
create a `cashier` role with a strong web-admin password and exactly **one**
organization grant. A Cashier has `shop:operate` only, not `shop:read`,
`shop:write`, `finance:read`, team, staff or administrator privileges.
A separate terminal assignment, granted by a Super Admin, is mandatory
before any register is visible or usable.

The database serializes cashier assignments on the subject account and
permits **one active terminal per cashier**, including across cities and
organizations. Revoking a terminal grant or suspending the account prevents
further authorized POS calls. An open shift must be closed and reconciled
before its operator's terminal assignment is revoked.

The cashier-only admin workspace exposes assigned register discovery and
shift opening/closing. Server-side return review and refund request actions
are reserved for managers. Site-level finance and invoices never become
visible through a cashier credential.

**Suspended cashier / open-shift recovery (draft):** The Super Admin
terminal setup view identifies any open shift and whether its assigned
cashier account is still active. After *explicitly suspending* a Cashier
account through Users, a Super Admin may record the physically counted
cash and select an incident reason via the dedicated supervisor workflow.
This is not allowed for active cashiers, managers, unrelated organizations
or invalid canonical sites.

The recovery function locks authority, cashier identity and the shift;
rejects unresolved payments, reservations, refunds and inconsistent
receipt state; computes expected cash exclusively from verified immutable
ledger events; and records the supervisor, original cashier, reason, count,
variance, unique request ID and security/transaction audits. **Cash
variance is preserved and flagged for investigation.** The operation is
idempotent only when supervisor, shift, reason, counted amount and request
ID match. No new sale, verified payment, receipt, Payabl refund or stock
movement occurs. Following a valid closure, the Super Admin can revoke
the old terminal assignment and appoint a replacement cashier.

**Independent second cash count (new Draft layer):** A separately
authenticated **second Super Admin**, not the original cashier and not the
first recovery closer, must independently count the physical cash for every
emergency-closed shift. The Super Admin review queue lists only the organization,
register, cashier identity, initial closer and review status; it deliberately
does not expose the first counted amount or expected cash before the second
reviewer submits their own result. The database then immutably records the
second independent count, reviewer, unique request ID and one of:

- `balanced_confirmed`: both physical counts agree with the immutable ledger.
- `variance_flagged`: the two physical counts agree with one another, but
  differ from the ledger; both the shortage/surplus and the review flag remain.
  This **does not settle or write off any money**.
- `count_disputed`: the physical counts disagree; the register remains under
  an explicit operational hold pending a separately authorized incident
  resolution (intentionally not yet implemented).

An emergency-recovered register cannot start **any** new shift, even through
an operator already assigned before suspension, nor receive an activated
operator while its independent count is missing or disputed. Once a matching
second physical count is entered (including a flagged ledger variance), normal
register assignment and shift opening are permitted, but the recorded variance
remains flagged for financial investigation. The review is once-per-shift,
immutable, replay-safe, organization-local and requires an active distinct
Super Admin session. It does not generate sales, payments, receipts or refunds.
Existing location managers remain limited to their own location finance; they
cannot read or submit organization-wide second-count reviews.

The review queue and second-count functions, like the recovery close, are
**default denied** at database level. The extended manual script
`ops/postgres/pos-shift-recovery-admin-grants.sql` requires explicit staging
approval and grants only specific SECURITY DEFINER functions to the admin
runtime role, never raw ledger tables or the mobile app role. The disputed-count
incident resolution, dual-device/physical custody verification and a formal
financial variance investigation protocol remain pre-production blockers.

The new function's EXECUTE permission remains private until explicit,
reviewed *staging* approval using
`ops/postgres/pos-shift-recovery-admin-grants.sql`. No production grant,
merchant activation or rollout has been performed. The organization
should separately approve financial discrepancy handling, dual-control
for physical cash counts, incident reporting and customer-facing receipts
before permitting production POS recovery.

The existing `pos.operators` code/PIN records remain **inactive** and are not
silently substituted for fully authenticated named cashier accounts. Checkout
and cash/card settlement still require explicitly approved grants, merchant
and fiscal configuration; this update does **not** activate live sales.

## Verification and release blockers

- Source-level regression tests: `frontend/tests/pos-academy-site-finance-boundary.test.mjs`.
- Global accountant role assertions: `frontend/tests/finance-global-role-boundary.test.mjs` and PostgreSQL `ops/postgres/finance-global-role-checks.sql` (run in CI).
- PostgreSQL structural RLS/definer checks: `ops/postgres/pos-cashier-ledger-checks.sql`.
- Optional and **operator-run only** staging grant script:
  `ops/postgres/pos-academy-site-admin-grants.sql`. Migrations do not grant
  the web or mobile runtime privileged table access.
- Before any merge or activation: run full .NET and Svelte checks, disposable
  PostgreSQL migrations, negative manager-forged city/site queries, concurrent
  terminal assignment/revocation tests, explicit grant checks and exact-SHA
  CI verification. Check whether all older POS endpoints still expose
  organization-wide data to any non-super-admin roles.
- Fiscal VAT/receipts, Payabl merchant recheck, card capture, refunds,
  return reconciliation and integration with Academy finance/general ledger
  are **NOT operational**. A checkout reservation is not a receipt.
- Do not enable terminal cash/card checkout, apply grants in production,
  connect production payment credentials, touch live Hostinger or merge
  this staging change based on source assertions alone.


## Cash-only MVP acceptance slice — 21 November 2026

A previously hidden end-to-end blocker was resolved: the original
`issue_verified_internal_receipt` accepted only manager and Super Admin
accounts, so a properly assigned named `cashier` could never finish
`settle_exact_cash_sale`. Migration
`20261115953000_PosCashierAtomicReceiptAuthority` accepts an authenticated
named cashier **only when they still own the exact sale, their assigned
register and the original open shift**. The authorization is rechecked
after locking the sale, and the private function is not granted to the web
or mobile runtime automatically. A closed shift, revoked cashier,
cross-location or other operator remains denied. Earlier manager behavior
is preserved.

Migration `20261115954000_PosCashierScopedSellableStock` adds a private,
bounded read-only catalog for an assigned operator's exact current shift,
drawing from `pos_cashier.stock` instead of conflating it with the separate
`pos.location_stock` inventory. It reports only product name, variant,
available quantity and public sale price; never supplier costs, other
sites' quantities, customer records or other operators' finances. Its
manual reviewed grant is added to
`ops/postgres/pos-cashier-shifts-admin-grants.sql`, while all sale
preparation, cash collection and receipt-issuance function grants remain
default **DENY** without separately approved fiscal controls.

The cashier screen now exposes a server-priced item selector (maximum
150 distinct sellable variants returned, up to 30 lines selected),
idempotent stock reservation, cancellation of an unpaid sale, exact
physically-counted cash and change, and a printable **stored** receipt.
The original cashier can open their own already-issued receipt through
`shop:operate`; no finance:read or finance:write capability is granted.

An acceptance scenario in `ops/postgres/pos-site-isolation-checks.sql`
uses synthetic items and a synthetic **exempt-only fiscal policy** in a
rollback-on-error subtransaction. It verifies one complete paid sale,
cash tender and change, immutable receipt, exact one-time reserved-stock
consumption, replay mismatch, shift reconciliation and denial of another
operator or city's inventory. Every synthetic payment, receipt, product,
stock record and fiscal policy is rolled back. This is reproducible
**disposable PostgreSQL test evidence, not permission to trade**.

Remaining POS production blockers:

- **Real VAT classifications:** the current checkout supports only a
  separately reviewed `verified_exempt` policy; taxable Cyprus goods
  need approved per-product VAT calculations and fiscal receipt details
  before sale. Never treat the synthetic exempt fixture as approval.
- **Stock ledger convergence:** `pos_cashier.stock` and the older
  `pos.location_stock` are separate inventories; physical opening
  counts, transfers, damage/restocking and source reconciliation must
  be signed off to avoid duplicate or phantom stock.
- **Independent sandbox UAT:** a realistic cashier/location manager
  runs paid/unpaid/replay/refund-return/receipt and shift scenarios in
  isolated staging, including consent and end-of-day reconciliation.
  An explicit reviewed grant and valid fiscal policy are required;
  this PR neither grants cash settlement nor changes live behavior.
- **Fiscal refunds:** a pending return review/refund instruction is not
  an executed cash refund, card reversal or compliant credit note.
  Legal receipt/credit-note output requires review.
- **Payabl:** merchant onboarding, signed server-side settlement,
  refunds, sandbox acceptance and strict source/merchant reconciliation
  are independent blockers. There is still no live Payabl card capture
  through the cashier panel.
- **Production readiness:** CI at exact SHA, two-person cash custody,
  backup/isolated restore, physical register/stock acceptance and
  operator sign-off are still required before any POS launch.

**No changes were made to production Hostinger, live payments, legacy
records or the live domain.**


## Inventory convergence audit — 21 November 2026

Two independent inventories currently exist for the same location and SKU:

- `pos.location_stock` receives inventory through the older auditable
  `pos.stock_movements` ledger. Its ordinary product/location assignments
  are tracked in `pos.location_products`.
- `pos_cashier.stock` reserves items on checkout and decrements stock only
  after the cashier receipt is atomically issued.

**Previously these inventories were not synchronized.** The initial
rollback-only acceptance test proved a paid cashier receipt decreased
`pos_cashier.stock` while `pos.location_stock` remained unchanged. New
migration `20261115956000_PosAtomicStockLedgerBridge` now links **future**
paid cashier receipt movements to `pos.stock_movements` using a distinct
`cashier_sale` type with an exact original cashier sale FK. The original
POS stock trigger validates and updates the older ledger inside the
**same transaction as cash verification, receipt and cashier inventory
consumption**. Reviewed legacy purchases, opening entries, sales/returns and
corrections mirror their movements back into cashier inventory, with a
private append-only `pos_cashier.stock_bridge_events` audit trail.

Both paths lock cashier stock before legacy stock in a consistent order.
Any missing location assignment, initial historical mismatch, insufficient
stock, double event or inconsistent movement fails the entire operation.
A failed receipt therefore does not retain a verified cash payment or
consume inventory. The movement bridge does not grant operators direct
table access or enable cash settlement.

This release adds migration
`20261115955000_PosSiteStockReconciliationRead` and an authorized
`GET /api/admin/pos/sites/{locationId}/stock-reconciliation` that compare
both inventories with a FULL OUTER JOIN **only at the caller's exact
authorized site**. Each SKU exposes original POS and cashier quantities,
reserved/available units, missing records, disabled location assignment
and quantity difference. The manager screen presents discrepancies and
the number of open shifts. A zero-SKU site is not considered reconciled.
No cross-city/organization data or supplier cost is returned.

A match of the two electronic balances is **not** physical-count evidence.
The response intentionally sets `physical_count_approved=false` and
`stock_ready_for_trading=false` until the separately reviewed opening
count, receiving/sales/returns stock movement mapping and human
approval are implemented and tested. Never silently initialize cashier
stock from the other ledger or directly overwrite a mismatch.

The site-specific read grant is listed in
`ops/postgres/pos-academy-site-admin-grants.sql` for manually approved
isolated staging use only; migrations still grant nothing to admin/mobile
runtimes automatically. The new route has an exact protected preview
allowlist; no financial or stock writer was added.

The disposable PostgreSQL acceptance test deliberately demonstrates
`legacy_missing`, a temporarily equal synthetic SKU, then **transaction
failure when the legacy balance is intentionally divergent**. With
matching stock, a two-unit paid cash sale and subsequently a two-unit
stock purchase now update both ledgers exactly once; replay cannot
duplicate either event. It also proves
that a location manager cannot read a sibling site's stock, a manager
from another city cannot read this site's stock, and a Cashier cannot
open management reconciliation. All test data is rolled back.

### Before POS launch

1. Verify the linked ledgers and source-of-truth policy on real
   **isolated staging**, including receiving, transfer, reservation,
   paid-sale consumption, voiding, approved returns/restocking, concurrent
   cashier sessions, manual corrections and recovery after partial failure.
   The new bridge covers documented movement kinds but does **not** claim
   full physical inventory reconciliation or complete refund/credit note
   readiness.
2. Reconcile every physical opening SKU and location to both financial
   and stock movement histories; investigate differences and obtain
   an independent approval with immutable evidence.
3. Verify transfer/damaged/returned goods, concurrent cashiers and
   refund/credit notes under isolated staging data.
4. Only then consider narrowly reviewed fiscal-policy and cash checkout
   execution grants. Payabl/card settlement remains a separate gate.

**No production inventory migration, historical balance adjustment, real checkout grant, Payabl
capture, production Hostinger change or production cutover was performed.**
