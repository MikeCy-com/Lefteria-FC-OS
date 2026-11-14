# News CMS: isolated private drafts

This increment follows the merged existing-published-article editor (#276). It
creates an independent, *unpublished* editorial workspace for both organizations
in the Lefteria FC OS rewrite.

## API

- `GET /api/admin/website/news-drafts?organization_id=...`: a bounded list
  of the latest 30 private drafts in the exact selected organization.
- `POST /api/admin/website/news-drafts?organization_id=...`: creates an
  unpublished draft with a server-generated `draft_<32-hex>` ID, version 1.
- `PUT /api/admin/website/news-drafts/{id}?organization_id=...`:
  replaces the draft's editable fields when `expected_version` matches.
  Stale writes return HTTP 409.

The first slice allows text drafts before content is complete; the title is
mandatory. The DTO contains a strict allowlist of safe article fields
(title/content/excerpt/image URL/category/audience/featured). Payloads reject
unknown and repeated JSON keys. The same-origin admin mutation header, JSON
content type and explicit 256 KiB request cap apply. Only HTTPS or existing
approved local media paths may be referenced, never arbitrary uploads.

## Security, isolation and publication

The dedicated admin database role has the only application privileges on
`public_content.news_drafts`: SELECT, INSERT and narrowly scoped UPDATE.
The public/mobile role has **none**, verified by the SQL permission test.
Every query uses the existing admin session/account/grant lock; every operation
rechecks the current role's `news:read` or `news:write` permission and exact
organization. Writes increment versions and insert an audit event atomically.

**No publishing endpoint exists in this increment.** Private drafts are not
read by `PublicContentService`, public site/news, the fan or parent mobile
application, AI, messaging, or any social delivery worker. Saving a draft
never publishes content or notifies anyone. Existing published article editing
remains independent and retains its established API.

The new DB migration is additive and does not touch imported documents,
public overrides, or import evidence. Its Down step intentionally refuses to
drop nonempty drafts or draft audits; restore through a reviewed forward
migration instead of discarding editorial work.

## Remaining parity

The next explicit feature must implement controlled **draft review/approval
and manual publication**, including who can approve, a public publication
record strategy, schedule/withdraw states, permissions, image moderation and
real multi-admin revision testing. Do not imply the current private workspace
publishes or schedules articles.

## Verification

The integration check exercises disposable PostgreSQL and real cookie API
flows for create, list, revise, stale version rejection, invalid JSON, owner
grant revocation and public feed isolation. All CI and guarded hosted preview
checks must pass independently before any merge/deployment. No production
migration or Hostinger deployment is authorized by this change.
