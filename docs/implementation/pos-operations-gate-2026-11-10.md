# POS operational gate — private location and inventory milestone

Status: **DRAFT / DEVELOPMENT ONLY**. Do not deploy, collect money, provision
live operators, or merge into main without an exact-SHA green release gate.
Original Lefteria-FC remains read-only.

## Source and current implementation

The original `backend/routes/pos.py` and `backend/pos_service.py`
represent a separate location-bound POS with cities, products, variants,
locations, operator codes/shifts, itemized cash/card sales and client sale IDs.
OS already had authenticated product/category administration, but **not**
a complete terminal sale, location stock, payment provider or receipt workflow.

This branch adds the following **concrete development code**:

- Private `pos.cities`, `pos.locations`, location-product availability and
  per-variant stock with composite organization and location foreign keys.
- Private operator and operator-session storage foundations. Passwords,
  plaintext PINs, bearer tokens and member identities are never accepted
  as POS operator credentials. **No operator-login endpoint is enabled.**
- Immutable EUR-cent sale headers, bounded line snapshots, per-location/day
  receipt numbering constraints, payment/refund evidence events, derived
  settlement balances, stock movements and serialized guards. All base
  tables are default-deny with RLS; no public/mobile/web-admin raw grants.
- Authenticated manager-only location read and city/location create endpoints,
  location-product optimistic assignment, idempotent positive stock receiving,
  and the corresponding branded `/admin/pos` controls.
- Owner/session double checks in SECURITY DEFINER functions, operator-only
  reviewed function grants, admin audit events and source-invariant Node tests.
  Rollback-only isolated PostgreSQL probes are wired into CI.

## Not yet implemented — REQUIRED before POS is complete

1. Private operator onboarding, code hashing with server-side pepper,
   rate-limited location-bound login, shift expiry, revocation and lockout.
2. Live cashier interface with actual product availability, separate operator
   realm, offline-safe idempotency and server-authoritative prices/quantities.
3. Atomic `catalog → location → cart → stock → sale → tender` checkout:
   lock every stock variant and operator session, calculate prices from the
   database, validate current location and receipt sequence, commit sale,
   stock events and payment evidence together, refuse insufficient inventory,
   duplicates and mismatched amounts.
4. **Card settlement** from an authenticated provider capture webhook.
   A browser redirect and unverified client claim must never mark a sale paid.
5. Receipt numbering and printable fiscal receipts, VAT/legal treatment,
   cash received/change, reconciliation, shifts and drawer closeouts.
6. Authorized return/refund endpoints with exact original sale linkage,
   provider refund evidence, restoration of the correct stock variant, and
   immutable correction audit.
7. Customer-facing website shop: consent-bound orders, stock reservations,
   delivery/pickup, checkout, returns and safe payment; keep this separate
   from POS terminal/operator authentication and Academy guardian billing.
8. Genuine import/mapping of original cities, locations, operators, shifts
   and sales into isolated staging; no migration from production was performed.

**Go-live tests:** execute .NET migrations/apply/rollback on disposable
PostgreSQL, Node/SvelteKit tests, API contract regeneration, 2-owner /
multi-location isolation, negative operator login, overpayment/underpayment,
duplicate sale requests, concurrent stock races, provider captures/refunds,
receipt numbering and end-to-end printed receipts. Source-level regex checks
are not runtime proof. The official GitHub runner has previously failed
before executing tests; check the exact current PR SHA before making claims.

Private inventory receiving is **not** a cash register or payment function.
Never present a location as open for POS merely because it was created.
