# POS catalogue backend release gate

This is a deliberately **private, schema-only** first step toward the legacy POS. The original read-only source uses five catalog/management collections (cities, categories, products, locations, operators), product variants with integer `price_cents` / `cost_cents` in EUR, operator assignment, immutable sale snapshots and location-bound checkout. It is **not** safe to port the old Mongo/React POS wholesale or expose a checkout without a separate transactional design.

## Implemented, not activated

- `pos.categories`, `pos.products`, and `pos.product_variants` are owner-scoped PostgreSQL tables; composite foreign keys prevent FC products from linking Academy categories/variants.
- Names, bounded source identifiers, integer EUR cents and optimistic `version` fields are constrained at the database boundary. Sale prices are 1–10,000,000 cents; costs are 0–10,000,000 cents. Product activation is stored but not yet editable.
- RLS is on without any permissive runtime policy. The schema and tables grant **no** privileges to public, application, operator, or web-admin runtime roles; there is no checkout, payments, code issuance, token sharing or sales ledger.
- A disposable PostgreSQL integration test verifies role denial, FC/Academy isolation, invalid price/duplicate variant rejection and transaction rollback. The migration refuses destructive rollback once catalogue data exists.

## Required before enabling any POS API or UI

1. Define and review legacy source import identity, categories, cities, product variants and stock/location mapping, including faithful product codes, currencies and active flags.
2. Implement narrow owner-checked, role-checked admin catalog reads/writes with fresh session authority, version conflict handling, bounded request JSON, transaction-level audit, SQL grants and real PostgreSQL tests. Do **not** grant the current API user direct table mutation.
3. Add a separate location-specific POS role and operator credential model; it must not reuse mobile OTP, website admin cookies, session secrets or bypass two-factor rules.
4. Before opening checkout, implement server-priced atomic/idempotent sale snapshots and strict EUR cents arithmetic, amount/quantity limits, receipt/access security, unique sale IDs and reconciliation tests. Payments and real transactions require a separate production acceptance review.
5. Add accessible Fira Sans/Fira Sans Extra Condensed and Lefteria black/gold admin pages only as each protected API is verified. Planned modules stay disabled rather than pretending sales occurred.

The original `MikeCy-com/Lefteria-FC` remains **read-only**; no Hostinger, production, original website or real financial data has been modified.
