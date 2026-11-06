# Parent Home dashboard — functional read-only slice

**Source reference (strictly read-only):** MikeCy-com/Lefteria-FC, frontend/src/mobile/pages/ParentDashboard.jsx, revision 91eafcc9efd01ad177b4f13a869cd5af7041e5b2.
**Target:** MikeCy-com/Lefteria-FC-OS only. Never push or merge into the original repository; production VPS deployment requires separate approval.

## Actual implemented behavior

- Replaces the Parent Home tab (/app?tab=home) placeholder with the core structure of the original: greeting, verified children, upcoming schedule, shortcuts and Academy banner.
- Authenticated identity/name use the existing mobile shell. Children come from the current PostgreSQL-backed GET /api/mobile/family endpoint, rechecked on each new mount.
- Only verified guardian-linked children appear, with explicit FC/Academy context. Each child's schedule button opens /app?tab=calendar&player_id=..., with server-side authorization independently enforced.
- Schedule is intentionally labeled for one child, **not** a fabricated combined-family feed. It reads existing private /api/mobile/calendar scoped to that linked child for the current club month. Current identity, child, organization and month must match before items render.
- Loading, denied, unavailable, empty and pagination states are distinct; an incomplete page is never presented as proof that no upcoming items exist.
- Chat, finance, invitation acceptance, player development, reactions and notifications are not yet ported and are not represented as operational: shortcuts are clearly labeled Under Developments.
- No database schema change, write action, provider call or production deployment.

## Verification and limits

frontend/tests/mobile-parent-home.test.mjs covers deterministic schedule selection, identity and child isolation, cancellation, sorting, SSR rendered states and route-level parent-role gates. All five existing CI jobs must pass before merge.

The full original parent dashboard remains incomplete; communication, finance, personal development and invitation lifecycle require new private, independently authorized feature slices. Hosted browser screenshot and real SMS-OTP login remain unverified.

Family/calendar data stays in the current authenticated controller only, not a new browser store, public page, or SEO feed. The OS source repository alone receives these changes.
