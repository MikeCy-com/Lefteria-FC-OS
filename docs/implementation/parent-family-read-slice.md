# Parent family: first verified read-only slice

**Source reference (read-only):** `MikeCy-com/Lefteria-FC` at `91eafcc9efd01ad177b4f13a869cd5af7041e5b2`, `backend/routes/family.py`, `backend/family_access.py` and `ParentDashboard.jsx`.
**OS implementation:** ASP.NET Core `GET /api/mobile/family` with existing JWT authorization and a scoped PostgreSQL query. Changes only in `MikeCy-com/Lefteria-FC-OS`.

## Implemented

- Current, active **parent-role** authentication is required. All five mobile role sessions use the existing JWT validation; fan, coach, player and management sessions cannot access family records.
- The response contains only `children: [{ id, name, organization_id, team_type, team_id, academy_group_ids }]`. The endpoint deliberately excludes phones, addresses, birth dates, payment records, attachments, medical notes and raw legacy source JSON.
- Identity ID, current verified phone, parent role, session expiry and player readiness are validated at the time of request. A **single PostgreSQL statement** joins persisted canonical contact bindings and current guardian-to-player relationship rows; a cached browser-selected child never grants access.
- Inactive, archived or transferred players cannot appear. All current verified sibling links appear under their actual FC/Academy organization and membership. A missing roster snapshot returns 503 rather than fabricated empty data.
- The service fails closed if more than 100 current linked children appear, rather than truncating unexpectedly.
- No database schema or runtime privileges are changed in this slice. All reads use the existing restricted application database identity.
- Cross-role, revocation, active-child and organization scopes are exercised in the disposable PostgreSQL HTTP integration fixture.

## Source features still missing

- Guardian invitations, accept/decline/cancel, controlled child phone changes, explicit revocation flow and grant audit history.
- Parent-facing family settings UI, dashboard, secure child invitation notifications and consent.
- Source-to-target import and reconciliation for invitation state, history and canonical phone ownership.

**Do not display write actions as operational.** Invitation and revocation migration must include transaction locking, explicit OTP proof, concurrency and replay rejection, audit event history, GDPR retention, minor privacy, role/org/record negative tests and a reviewed schema migration.

## Rollout gate

Do not interpret this read-only API as full parent/family parity. Only merge into OS `main` after .NET build, OpenAPI client generation, PostgreSQL integration tests and container smoke succeed. Never merge or modify `MikeCy-com/Lefteria-FC`. This PR does not deploy to VPS or contact families.
