# HUB admin draft creation — unpublished and audited

Date: 2026-11-10. Stacked after PR #161 (admin metadata reader), PR #160 (private schema), PR #159 (approval/subscription policy), PR #158 (search) and PR #157 (HUB navigation).

## Implemented API

`POST /api/admin/hub/editorial?organization_id=lefteria_fc_academy` (or `lefteria_fc`) accepts a bounded JSON draft with `kind`, `title`, `body`, `audience_parent`, `audience_player`, `age_min` and `age_max`. Requires web-admin cookie authentication, exact active organization grant and `news:write`. Validated drafts create `hub.content` in `ai_draft` state with provenance `human`, `hub.revisions` revision 1, a review event and a separate audit security event as one PostgreSQL function transaction. Returns `201` with limited metadata, **never draft body**.

The SQL routine `hub.create_admin_draft` validates fields independently, locks session/account/organization authority against concurrent revocation, rejects disallowed roles, and runs with fixed `search_path`. Function EXECUTE is explicitly granted only to the dedicated admin role. Neither admin nor mobile runtime receives raw HUB table access. The SQL grants checker and boundary script enforce that separation.

## Operational boundaries

- This is human-admin **draft creation**, not an AI-generation endpoint. An AI service can only submit via separately reviewed job credentials and cannot impersonate an admin. Content is never auto-published.
- No image upload, approval, publishing, fan paid content, Payabl checkout, or entitlement API is enabled by this PR.
- Publication requires separately implemented text and actual visual review, version checks, role and org scoping, rejection and audit.
- The backend returns no draft bodies in a response; private previews require a future restricted revision API.
- No migrations or grants were run against production.

## Verification gates

Run full .NET 10 compile, SvelteKit tests, OpenAPI generation, the new Node contract tests and disposable PostgreSQL migration/grant checks at exact branch head. Test valid human manager/editor and invalid role, revoked cookie, revoked organization grant, other owner, expired session, invalid body/audience/age, rollback on audit failure and idempotency semantics. Do not merge until these pass; node tests committed but **not executed** in this tool session.
