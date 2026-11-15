# HUB administration: human drafts and truthful fan billing status

**Scope:** source-only UI improvement in Lefteria-FC-OS; no Hostinger deployment or live billing.

## Admin editorial workspace

- /admin/hub exposes the already-implemented POST /api/admin/hub/editorial to permitted managers, super administrators and editors. It creates a private, human-provenance ai_draft for exactly the signed-in, authorized First Team or Academy organization.
- The form collects kind, bounded title/body, parent/player topic flags and optional age limits. It rejects empty/unbounded/control-character text and requires at least one audience. The existing server and SQL function independently validate authorization, data and state, and audit the write.
- New drafts remain unpublished. The workflow continues to require separately reviewed text and visuals, manager approval and an explicit publish operation. No automatic AI approval is introduced.
- The editor can filter up to the latest 100 organization-scoped records by status; this does not imply a complete historical catalog or broaden access.
- **Current text restriction:** the existing API and PostgreSQL validator reject all control characters including newlines. The form explains this rather than quietly transforming user content. Rich-text and revision editing need a separately reviewed backend contract.

## Fan subscriptions are not editorial permissions

The page presents a concise boundary note: Payabl fan subscription is sandbox-only, and choosing parent or player topics never changes the fan's identity role or child/guardian access.

Existing Payabl integration uses signed callbacks and verified finance capture before granting a fan lesson entitlement, with distinct lesson 1 schedules per topic and per subscriber. **This change does not create an admin subscription ledger, recurring stored-card payments, refunds UI, plan configuration, or webhook handling.** The pricing/worker setup must remain isolated staging with explicit operator approval per docs/implementation/hub-payabl-sandbox-and-weekly-delivery-2026-11-13.md.

## Acceptance

- Verify Svelte compilation and Node test suite in CI.
- Use a disposable, isolated staging database and test that a human editor can create an org-owned unpublished draft, a foreign-org user cannot, and no fan accesses content prior to provider-confirmed capture and publication.
- Confirm the /admin/hub page still never allows editor/AI account to approve or publish. Keep production unchanged.
