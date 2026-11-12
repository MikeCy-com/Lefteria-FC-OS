# HUB submit-for-review API — no approval or publication

**Date:** 2026-11-10. Stacked on PR #162 → #161 → #160 → #159 → #158 → #157. **Development only.**

## Implemented

`POST /api/admin/hub/editorial/{id}/submit-review?organization_id=lefteria_fc_academy` accepts a JSON `{ "version": 1 }` body and requires an authenticated current web-admin session, `news:write` permission, and exact current organization assignment. A PostgreSQL `SECURITY DEFINER` routine independently locks and verifies that admin session, account and organization grant, then locks the exact draft row, checks `ai_draft` and expected version, and makes one **atomic transition to `review_required`**. A review event and a security audit event are written in the same transaction.

A stale version or repeated submission returns conflict; wrong organization does not disclose another organization's draft. The response contains only id, organization, status and incremented version; it does not contain the draft body or unpublished visuals.

The routine is callable only by the dedicated web-admin role through an explicit SQL grant; PUBLIC and the mobile-runtime role are denied. A privileged-function and raw-table-access check was extended accordingly.

## Still unavailable

- No admin **draft detail/body preview** API, so a reviewer cannot yet inspect the actual full content securely.
- No AI text or image generation worker, asset quarantine/upload workflow, text/visual moderation, final approval, publish/reject/revise APIs or subscription checkout.
- A `review_required` status is **not** approval; the publication fields stay null. No automatic feed or notification occurs.
- No migration was run on Hostinger or the legacy system. The role grant requires an explicit operator step in a disposable environment first.

## Verification before merge

Run .NET 10 compile/OpenAPI, test Node assertions, apply migrations and separate admin grant bootstrap to disposable PostgreSQL, run boundary scripts, then test 401/403, wrong organization, revoked session/grant, stale/duplicate version, same-draft parallel submissions, audit rollback, and no direct database access. The committed assertions are **source-level only**, not a successful runtime/CI check. Keep this PR draft and do not merge or deploy until verified.
