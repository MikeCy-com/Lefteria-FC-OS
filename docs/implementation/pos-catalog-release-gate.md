# POS catalog backend release gate

The old `MikeCy-com/Lefteria-FC` repository is a **read-only** functional reference. It models cities, categories, products, locations, operators, immutable sale snapshots, EUR cents and location-specific checkout. The OS must not port its MongoDB checkout or payment semantics without a separately verified transaction design.

## Implemented

- The private PostgreSQL foundation contains `pos.categories`, `pos.products` and `pos.product_variants`. Composite owner foreign keys prevent an FC product from referencing Academy categories or variants.
- Integer `cost_cents` / `price_cents`, original-style bounded identifiers, active flags and nonnegative versions are enforced by SQL constraints. Owner roles never receive POS schema or table mutation rights. Empty-only rollback protects catalog data.
- **One read-only POS catalog path:** `GET /api/admin/pos/catalog?organization_id=...` requires the admin-cookie authentication scheme, current `shop:read`, and the specific current first-team or Academy grant. A separately verified security-definer function rechecks session, role, grant and owner under a row lock, returning only bounded categories/products/variants. The admin-auth SQL role receives **USAGE on the POS schema and EXECUTE on that one function**; neither that role nor the mobile runtime role receives raw POS SELECT/INSERT/UPDATE/DELETE privileges.
- The Fira Sans/gold-on-black `/admin/pos` screen is **read-only** and re-fetches on role, organization and session changes. No fake sales, payments, operators or pending writes are implied. An uninitialized/empty catalog has a genuine empty state.
- CI validates PostgreSQL runtime denial, direct function spoof refusal, FC/Academy isolation, fresh revocation, variant prices, response typing and read-only API methods. The schema grant-check scripts reject unexpected privileged POS functions.

## Still blocked before catalog writes / sales

1. Implement reviewed admin create/edit APIs for categories/products/variants with exact role and owner scope, bounded validated input, optimistic version conflicts and atomic audit. **Never** grant the API role direct mutation on POS tables.
2. Map legacy city/location inventory and product assignment per owner; require a separate operator realm, strong private code lifecycle, and locked location-specific availability.
3. Prior to any POS checkout: server-price every product/variant, forbid client price authority, build a durable idempotent sale snapshot/receipt and transactional audit, enforce quantity/amount limits, and verify reconciliation in PostgreSQL.
4. Keep CRM consent-bound enquiry workflows separate from POS customers and private minors' information. Payments require separate release authorization and production compliance review.
5. Preserve brand parity with the parent-role home; no production deployment, Hostinger changes, live transactions or source-repository mutations are authorized here.
