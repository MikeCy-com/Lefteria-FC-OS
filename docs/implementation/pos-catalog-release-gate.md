# POS catalog backend release gate

The old `MikeCy-com/Lefteria-FC` repository is a **read-only** functional reference. It models cities, categories, products, locations, operators, immutable sale snapshots, EUR cents and location-specific checkout. The OS must not port its MongoDB checkout or payment semantics without a separately verified transaction design.

## Implemented

- The private PostgreSQL foundation contains `pos.categories`, `pos.products` and `pos.product_variants`. Composite owner foreign keys prevent an FC product from referencing Academy categories or variants.
- Integer `cost_cents` / `price_cents`, original-style bounded identifiers, active flags and nonnegative versions are enforced by SQL constraints. Owner roles never receive POS schema or table mutation rights. Empty-only rollback protects catalog data.
- **POS catalog read path:** `GET /api/admin/pos/catalog?organization_id=...` requires the admin-cookie authentication scheme, current `shop:read`, and the specific current first-team or Academy grant. A separately verified security-definer function rechecks session, role, grant and owner under a row lock, returning only bounded categories/products/variants. The admin-auth SQL role receives **USAGE on the POS schema and EXECUTE on that one function**; neither that role nor the mobile runtime role receives raw POS SELECT/INSERT/UPDATE/DELETE privileges.
- The Fira Sans/gold-on-black `/admin/pos` screen is **read-only** and re-fetches on role, organization and session changes. No fake sales, payments, operators or pending writes are implied. An uninitialized/empty catalog has a genuine empty state.
- CI validates PostgreSQL runtime denial, direct function spoof refusal, FC/Academy isolation, fresh revocation, variant prices, response typing and read-only API methods. The schema grant-check scripts reject unexpected privileged POS functions.

## Still blocked before catalog writes / sales

1. **Category create/update is implemented** by audited `POST /api/admin/pos/categories` and `PUT /api/admin/pos/categories/{id}` with exact organization and shop:write authority, strict body validation, optimistic versions and a single atomic PostgreSQL category+audit function. Product and variant editing remain blocked. **Never** grant the API role direct POS table mutation.
2. Map legacy city/location inventory and product assignment per owner; require a separate operator realm, strong private code lifecycle, and locked location-specific availability.
3. Prior to any POS checkout: server-price every product/variant, forbid client price authority, build a durable idempotent sale snapshot/receipt and transactional audit, enforce quantity/amount limits, and verify reconciliation in PostgreSQL.
4. Keep CRM consent-bound enquiry workflows separate from POS customers and private minors' information. Payments require separate release authorization and production compliance review.
5. Preserve brand parity with the parent-role home; no production deployment, Hostinger changes, live transactions or source-repository mutations are authorized here.

## Audited product-writer milestone (backend only)

The admin product POST/PUT backend now accepts **one entire product with 1–16
explicit cost/price variants**, validates strict nested JSON and unique
variant codes, locks current admin cookie/role/organization in PostgreSQL,
checks the current expected product version, replaces its variants atomically,
and appends an audit event in the same transaction. Only an exact reviewed
security-definer product function receives EXECUTE; raw tables remain denied
to admin/mobile/public runtime roles. Product and variant write failures must
leave the previous snapshot unchanged. This does **not** grant POS operator
access, enable sales, or publish any new product to a shop automatically.

The next distinct release gate is a source-faithful, accessible
product-management form with refreshed catalog versions and explicit FC/
Academy ownership. Public merchandising, stock by location, pricing used for
actual checkout, receipts and payment collection remain separate disabled
workstreams and require transaction and security reviews.

## Unsubmitted basket preview (2026-11-10)

The POS admin catalog now includes `AdminPosBasketPreview.svelte`: an entirely client-side, nonpersistent basket preview fed **only by the authenticated verified catalog**. It selects active products in active categories, exact variant IDs, bounded quantities (1–99 per line) and up to 30 lines, computes EUR-cent preview totals from catalog prices, and resets on organization/catalog changes. It has **no POST, sale, payment, receipt, tax authority, inventory mutation or persistence**. The displayed total is indicative, not an invoice or checkout quote; the eventual server checkout must independently revalidate authorization, location, stock, tax rules and current product price in a single durable idempotent transaction. Structural regression checks are in `frontend/tests/pos-basket-preview.test.mjs`.

This is an incremental POS operator workflow, **not a completed POS**. Register/session/operator/location setup, inventory movements, immutable sale ledger, receipt issuance, refund and reconciliation remain disabled. No production deployment or live payment collection is authorized.
