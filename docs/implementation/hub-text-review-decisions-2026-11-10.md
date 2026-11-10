# HUB manager-only text review decisions

Date: 2026-11-10. Stacked after PR #164 → #163 → #162 → #161 → #160 → #159 → #158 → #157.

## API added

`POST /api/admin/hub/editorial/{id}/text-review?organization_id=lefteria_fc_academy` accepts JSON `{"version": 2, "decision": "passed"}` or `"rejected"`. An authorized manager or super_admin may decide **only the latest stored text revision** after its draft was submitted (`review_required`). The endpoint checks the current web-admin session and exact organization assignment. The SECURITY DEFINER SQL routine independently checks and locks the session/account/grant and the organization-owned content row.

The decision is audited in both `hub.review_events` and `audit.security_events` in the same transaction, with optimistic content-version increment and conflict protection against repeat/parallel actions. Content stays `review_required`; **visual_review is untouched**. No actor may approve visuals or publish through this endpoint.

## Important media limitation

The HUB media schema currently records `storage_key`, hashes, moderation and review metadata, but has **no connected quarantine object-storage reader or authorized image stream**. The draft-detail endpoint returns safe descriptors only. Approving a visual based only on description or AI output claims is prohibited. Build sanitized upload and private, authenticated preview delivery with strict content hashes and content security headers before any `visual_review=passed` operation is permitted. The AI generator still must not publish, review its own output, or gain admin credentials.

## Fan HUB and privacy

Fan subscriptions still require backend-confirmed Payabl entitlement and requested audience on every read. None of this text-review function grants any fan/player/parent access or returns paid content.

## Verification before merge

Run .NET compile/OpenAPI and structural Node tests; run disposable PostgreSQL migration/grants/boundary scripts. Test manager and super_admin success, editor/coach/marketing denied, stale and duplicate version conflicts, cross-organization 404, session or organization grant revocation, atomic rollback on audit failure, unchanged visual_review and null publication fields. No test execution, merge, Hostinger deployment or production migration happened in this turn.
