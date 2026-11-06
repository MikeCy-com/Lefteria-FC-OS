# Parent mobile children tab — implemented read-only stage

Source reference is `MikeCy-com/Lefteria-FC` at pinned commit `91eafcc9efd01ad177b4f13a869cd5af7041e5b2`. Original repository is **read-only**. All implementation is in `MikeCy-com/Lefteria-FC-OS`.

This adds the parent-role `/app?tab=children` interface, backed by the authenticated and PostgreSQL-tested `GET /api/mobile/family` endpoint introduced in OS PR #32.

## Functional behavior

- Parent-only navigation and view, preserving the existing app shell, role switcher, header, bottom navigation and no-parent-composer rule.
- On view mounting, request an up-to-date guardian-linked roster. Show a loading state, meaningful error/retry, verified-empty state or actual linked children with FC/Academy context. The data is never mocked.
- The controller uses the generated OpenAPI client and only its active bearer token. It validates every returned field and organization, rejects duplicate/malformed children, clears earlier data before refresh, discards late responses on logout/role changes, and never persists family data into browser storage.
- The interface shows only child display names, membership category and a safe calendar navigation action. It does not display raw child IDs, private phones, birth dates, financial records or medical details.
- Unported family invitation, acceptance, revocation and phone-change interfaces stay explicitly marked **Under Developments**, without pretending the actions exist.
- No new backend endpoint, SQL grant, database migration or provider side effect.

## Verification and remaining acceptance gates

`frontend/tests/mobile-family.test.mjs` exercises the real mobile controller's authenticated fetch, wrong-role denial, live revocation response, malformed payload handling, stale response after logout and SSR component states.

Before merge to OS `main`: all five CI jobs pass, including SvelteKit check/tests, .NET contract compatibility, disposable PostgreSQL tests and Docker runtime. Desktop/mobile visual screenshot equivalence against the original parent views is a separate pending gate.

The original parent dashboard has much broader content (financials, notices, development, attendance and invitation controls). **This is a read-only children tab and not the complete parent dashboard.** Issue #31 remains open for secure invitations and full family parity. No code should ever merge to `MikeCy-com/Lefteria-FC`; only to verified OS `main`. Production deployment still requires separate approval.
