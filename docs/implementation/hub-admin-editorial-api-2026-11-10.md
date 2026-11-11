# HUB editorial API: first protected server reader

Date: 2026-11-10. Stacked on PR #160 (private schema), PR #159 (approval/paywall policy), PR #158 (search), PR #157 (HUB navigation). No production deployment.

## Implemented

`GET /api/admin/hub/editorial?organization_id=lefteria_fc_academy` (or `lefteria_fc`) is an authenticated web-admin endpoint. It returns at most 100 **organization-owned editorial metadata** items: id, organization_id, kind, title, status, version, provenance, age and audience flags, changed/approved/published timestamps. It deliberately **does not** return draft body text, media bytes/storage paths, subscriber identities, billing state, or private child data.

Security is checked twice: (1) fresh cookie session + `news:read` capability + explicit current organization grant in C#, (2) current unrevoked admin session, active admin account, exact organization membership and eligible admin role in a `SECURITY DEFINER` PostgreSQL function. SQL limits output to 100 rows; the API validates response owner, shape and row count. No raw HUB table access is granted to the admin or mobile runtime role.

Migration `20261110183000_HubAdminDraftReader` adds only `hub.read_admin_editorial(text,text)`, revokes PUBLIC execution, and changes no table data. The explicit operator grant file grants this function to the dedicated web-admin role and adds it to the known-function and privilege-boundary allowlists. Role and no-table-access checks are in `ops/postgres/admin-hub-editorial-boundary.sql`.

## Explicit omissions

This is **not** creation, AI generation, editing, media review, approval, publication, subscription checkout or member article delivery. The earlier `hub` tables are initially empty and intentionally private; this reader returns `items: []` until a separately reviewed editor/AI worker creates actual content. No published member or fan API has been enabled.

## Verification required before merge

1. Compile the exact branch with .NET 10, generate/check OpenAPI and run existing regression tests; run the newly committed structural Node tests.
2. On disposable PostgreSQL, apply migrations #160 and this PR; invoke the role-specific bootstrap/grant script using dedicated test roles; run the boundary check and rollback.
3. With synthetic users, assert unauthenticated 401, wrong role 403, wrong org 403, revoked admin/grant denied, invalid org 400, valid editor/manager authorized, 100-item cap, and no body/media/subscription fields.
4. Assert no mobile runtime privilege on `hub` schema/tables or security-definer function and no direct admin HUB table SELECT.
5. Exercise concurrency with admin session/grant revocation and inspect query performance; browser admin wiring can follow after the server contracts are confirmed.
6. Keep the feature unmerged and undeployed until exact-head CI evidence and review. Existing GitHub Actions runner-start failures may still block CI.

**No source Lefteria-FC repository writes, live provider payments, generated AI content, or Hostinger deployment.**
