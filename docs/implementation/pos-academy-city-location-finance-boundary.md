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

**Important limitation:** the existing cashier shift and settlement APIs
accept authenticated manager/super-admin accounts, not a least-privilege
standalone cashier account. This slice supports distinct **existing authorized
manager identities** on different registers; it does not create independent
cashier PINs, automatically grant broad manager privileges or activate the
private `pos.operators` code-based identity table. A dedicated restricted
cashier role/session lifecycle remains a separate required implementation
before general operators should be onboarded.

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
