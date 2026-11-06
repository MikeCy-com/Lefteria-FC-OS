# Shop/catalog parity — read-only first release

**Original source:** `MikeCy-com/Lefteria-FC` at `91eafcc9efd01ad177b4f13a869cd5af7041e5b2`,
`frontend/src/pages/NewShopPage.jsx` and `ShopPage.jsx`.

**OS implementation:** `/shop` in SvelteKit; `/shop?tab=merchandise` for products;
`/shop` defaults to tickets, matching the original tab behavior.

## What is functional in this slice

- Public tickets (match / seasonal) and merchandise are rendered from the existing
  generated `/api/public/sections` contract for the explicitly named FC and
  Academy organizations. No random/demo entries or hard-coded prices are used.
- Prices parse numeric public values to EUR; invalid, negative or implausible
  amounts never display as ordinary prices.
- The FC and Academy projections cannot inject records owned by a third or
  mismatched organization. Shared logical IDs are shown once, favoring the FC
  public projection, and the UI retains organization labels.
- Public site chrome, navigation, SSR, keyboard-readable tabs, live inventory
  indicators, size options and a responsive black/gold layout are present.
- An unavailable projection renders an explicit availability error, **not** a
  misleading claim that no products or tickets exist.
- Public reads use the existing credential-free server client, with its bounded
  timeout and known internal API origin.

## Not yet implemented — hard release blockers for e-commerce parity

- Customer email/password identity, durable shopping basket, quantity/size
  selection, inventory reservation, order creation, checkout, gateway and webhook
  integration, refunds, ticket fulfilment, delivery and sales audit.
- Admin inventory, product publishing and ticket-management writes.
- Payment authorization and idempotency/replay safeguards.
- Live provider sandbox and end-to-end payment/fulfilment verification.

The UI explicitly labels checkout **Under Developments** and provides **no
fake purchase buttons or acceptance form**. Browsing can be merged when CI
passes, but this does **not** mean e-commerce parity or production release
approval has been achieved.

## Acceptance evidence required

The new `frontend/tests/public-shop.test.mjs` covers real page SSR with a local
HTTP public API fixture, tab selection, price formatting, per-organization
filtering, deduplication, bounded no-cookie public requests, HTML escaping and
503/empty distinction. All ordinary .NET, SvelteKit, API contract and Docker
jobs must stay green.

Future commerce PRs need scoped customer authorization, immutable money/quantity
handling, transactional inventory, gateway signature validation, sandbox-only
integration, idempotency and negative test cases. No real checkout or external
provider call is approved by this catalog slice.

**Repository safety:** The original Lefteria-FC GitHub repository and any
production MongoDB source stay entirely read-only. Approved code changes merge
only to `MikeCy-com/Lefteria-FC-OS/main`; VPS deployment is separate.
