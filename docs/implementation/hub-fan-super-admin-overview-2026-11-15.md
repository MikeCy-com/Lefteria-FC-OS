# HUB fan subscriptions: aggregate Super Admin overview

**Date:** 2026-11-15. **Target:** Lefteria-FC-OS only, development branch. No production deployment or changes to the original Lefteria-FC repository.

## Implemented vertical slice

A new read-only /admin/hub/subscriptions page and private GET /api/admin/hub/fan-subscriptions/overview?organization_id=... endpoint display:

- Per-organization sandbox plan metadata (parent / player / both, EUR cents, 30-day period and enabled flag).
- Counts of stored pending, active, past-due, cancelled and expired fan subscription records.
- **Verified-active** count separately rechecking subscriber is still an active fan, the exact original Payabl checkout ledger captures EUR payment with matching actor, owner, track and provider transaction, and no signed refund or reversal, and both recorded subscription and payment windows are unexpired.
- The read is aggregative: no subscriber identity, name, phone, child link, provider reference, checkout URL, receipt, invoice or per-payment row is returned.
- The UI explicitly differentiates recorded active vs actually verified access and does not represent a checkout redirect or pending charge as successful payment.

This is an inspection dashboard, NOT a subscriber directory, a finance reconciliation ledger, a recurring-billing implementation or a back-office plan price editor.

## Security

- **Super Admin only**, with server-verified current web-admin cookie, live finance:read, exact organization assignment, and an additional PostgreSQL SECURITY DEFINER verification that rechecks live role, grant, active account and session (row locks serialize concurrent revocation).
- No city/location manager, coach, editor, marketing user or accountant can read organization-wide fan subscription aggregates. A future accountant view requires its own financial scope and explicit owner/location policy.
- The new SQL function grants EXECUTE only to the dedicated admin-auth role. Neither web-admin nor mobile runtime receives raw table access to subscribers or payment ledger. A disposable PostgreSQL privilege probe checks SQL ownership, fixed search path, runtime grants, no PUBLIC execution, and raw table denials.
- The endpoint is intentionally outside public OpenAPI, is available only behind a reviewed exact GET edge path, and sends private, no-store.
- No subscription state write, payment capture, price change, refund, SMS, AI publishing or other mutation occurs.

## Staging acceptance

1. On **isolated, disposable PostgreSQL**, apply migration 20261115920000_HubFanAdminOverview, run web-admin grants and the new privilege check. Confirm rollback and reapply.
2. Confirm anonymous -> 401, accountant/editor/manager -> 403, valid Super Admin with wrong organization -> 403, revoked session/account/org grant -> denied; valid same-organization Super Admin -> 200 with only aggregate fields.
3. Seed synthetic fan subscriptions for pending, paid captured, failed checkout, expired, role-swapped and signed refund/reversal; verify only captured unexpired unrefunded active fan records contribute to verified-active.
4. Ensure Academy and First Team counts and plan prices never cross, and an active row without signed finance proof does not count verified.
5. Run exact-head Svelte/Node, .NET, OpenAPI consistency and browser CI. Keep live Hostinger unchanged; no Payabl sandbox credential activation is done by this change.

## Follow-on work (not implemented here)

A real subscription admin ledger with subscriber search, invoice access, manual refunds, renewal management, consent and data retention requires separate per-account entitlements and audited finance policy. Automatic recurring cards, webhook replay testing in isolated Payabl sandbox, notification delivery and provider security/privacy review must be separately approved before production readiness.

## Parallel migration note

The latest main branch contains two separate migrations with the timestamp 20261115910000 (CRM registration inbox and player-goal territory origin). This change deliberately uses 20261115920000 for the HUB migration to avoid adding a third collision. The pre-existing duplicate in main needs an independently coordinated migration-history correction before production readiness; do not silently rewrite migration history on an already deployed database.
