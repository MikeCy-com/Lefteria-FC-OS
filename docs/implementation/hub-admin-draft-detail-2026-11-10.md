# HUB admin draft detail: private text and visual review descriptors

**Date:** 2026-11-10. Stacked after PR #163 → #162 → #161 → #160 → #159 → #158 → #157. This is development code, not a deployed feature.

## Added

`GET /api/admin/hub/editorial/{id}?organization_id=lefteria_fc_academy` (or `lefteria_fc`) retrieves the latest stored editorial revision for an authenticated, authorized admin. The result contains draft title, text, version, publication state, audience/age flags, text/visual review states, and strictly redacted visual descriptors: ID, kind, MIME type, alt text, moderation status and AI provenance. **No media object key, file hash, binary data, subscriber identity, provider reference or payment information is returned.**

Authorization happens twice: fresh cookie `news:read` and exact owner membership at the endpoint, plus current account/session/organization grant in a tightly scoped `SECURITY DEFINER` PostgreSQL function. A draft from another organization is not revealed. The function performs no update. Only the dedicated web-admin role may execute it; the mobile role and PUBLIC are denied. Existing no-direct-HUB-table-access checks remain.

## What is still missing

The endpoint provides **review text and descriptors, not actual preview images**. Visual approval cannot be enabled until a secure media-delivery API renders the quarantined image to authorized reviewers, accompanied by integrity, consent and moderation safeguards. The AI agent cannot approve, publish or grant access. Final text/visual reviewer decisions, content publication, fan subscription and Payabl processing are still separate work.

## Verification

- Run .NET compilation, OpenAPI checks, Node structural tests and PostgreSQL migrations/grants in an isolated disposable test database.
- Test 401/403 and 404, wrong organization, expired/revoked admin session, revoked org grant, cross-org title or body leakage, oversized media collections, role denial and absence of raw table privileges.
- Verify a stored draft's body matches the latest revision; perform explicit media descriptor checks and absence of storage keys/provider details.
- Verify no caches or log sinks expose unpublished text. Content remains unavailable to public, player, parent and fan readers.
- Keep all dependent PRs draft/unmerged and do not deploy to live Hostinger or modify the source Lefteria-FC.

No live migration or integration test was executed in this turn.
