# Delegated football territories and verified roster metrics

Implemented in Lefteria-FC-OS only; Hostinger production and the legacy
Lefteria FC repository remain untouched.

## Authorization

- \`GET /api/admin/football/territories/access\` lists active accounts already
  granted the requested organization and their explicit city, location or team
  delegated roles. Accessible only to a **super admin** with a fresh
  organization-granted web-admin session. Excludes credentials, phone numbers
  and private roster.
- \`PUT /api/admin/football/territories/access\` grants or revokes one verified
  role for an exact territory. Manager accounts can receive city/location/team
  manager grants; coach accounts receive coach grants; editor and matchday
  editor accounts can receive staff grants. Subject must already have an
  active account and an organization grant. No cross-organization escalation.
  The database locks the target account and org grant and records a security
  audit event on every operation.
- \`GET /api/admin/football/territories/metrics\` produces site-specific
  canonical **active player** and **group** counts without names, attendance,
  financial data or notes. Site managers see only sites explicitly granted
  to them; city managers see only locations within their city; superadmins
  with Academy access see all sites.
- The authenticated \`/admin/territories\` page now includes these statistics
  and Super Admin controls for selecting eligible users, grant territory and
  role, viewing existing grants, and confirming revocations. The mobile app
  does not receive these private admin functions.
- Migrations contain the functions; an operator must separately apply
  \`ops/postgres/football-territory-delegation-admin-grants.sql\` after the
  prior territory catalog grants. CI proves isolation with rollback-only
  synthetic sessions, personnel, rosters, cities and sites.

## Financial integrity boundary

Financial issues and settlement events are immutable independent ledgers.
Historical fees cannot be attributed to a city/location merely because a
player is *currently* in a site's group: memberships can change, and one
player may have multiple groups. A future migration must record independently
verified **issue-time** location attribution, preserve legacy unallocated
fees, prevent double-counting and connect Payabl receipts/cash settlements.
This slice intentionally does not display unreliable payment statistics,
rewrite invoices, or alter Payabl billing behavior.

## Release notes

No live production changes and no automatically assigned permissions.
Explicit manager/staff grants are required; organization-level manager access
by itself does not reveal any other territory. Browser audit uses synthetic
authentication and cannot prove original screenshot pixel parity.
