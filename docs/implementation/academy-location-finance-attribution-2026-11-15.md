# Academy location finance attribution — 15 November 2026

The source of truth for invoices and Payabl callbacks remains the existing immutable
finance ledgers. This migration adds separate attribution records and does not alter
a payer's invoice, original amount, status, capture or cash receipt.

- Current-month newly issued Academy training fees receive an automatic location
  snapshot only if every roster membership is tied to the same single active
  canonical city/location. Advance or backdated fees, legacy/unassigned groups,
  contradictory locations and old invoices remain **unallocated**.
- General charge issues are only attributed after explicit Super Admin evidence
  review. Existing Academy training fees can likewise be manually verified if
  no location was captured at issue time. Every manual attribution requires the
  immutable issue ID, verified location and evidence reference; the reference is
  hashed in the database and the action audited. An already-attributed fee cannot
  silently move to a different site.
- Location managers see only authorized site metrics; city managers only their
  own city's sites; super admin all authorized Academy sites. Training-fee figures
  and general-charge figures deliberately remain separate because their
  historical sources can overlap. The location view does **not** sum them into
  a misleading single revenue total. General charge paid figures come from the
  immutable settlement ledger; training fee paid status comes from the already
  verified fee status.
- The Super Admin backlog shows unallocated records (maximum 100 rows/month)
  for verified manual reconciliation. Site managers cannot enumerate those records.
- The admin page is `/admin/territories` under an Academy city and location.
  The operator must run the EF migration and then approve
  `ops/postgres/academy-location-finance-admin-grants.sql` using dedicated
  authenticated web-admin and mobile roles on staging. Access to the new
  attribution tables is never granted directly.
- CI runs disposable PostgreSQL tests for verified locations, ambiguous fees,
  immutable attribution, authorized site/month aggregates, paid cash ledger,
  duplicate prevention, revocation and cross-organization exclusion.

**Limitations:** Historical fee assignment requires human evidence, not guesses
from current membership. Existing first-team payments, new staff reporting,
fixture publication and pixel-perfect screenshot parity remain separate work.
No Hostinger production deployment is performed.
