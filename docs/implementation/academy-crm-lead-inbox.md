# Academy CRM enquiries — admin source parity slice

Status: **separate OS development branch only**, no production deployment, no contacts sent and no modifications to `MikeCy-com/Lefteria-FC`.

## Source behavior and boundaries

The original Lefteria FC repository has `RegistrationsTab.jsx`,
`PlayersCRM.jsx`, `MarketingTab.jsx`, `backend/routes/players_crm.py`
and Academy registration pages. This initial OS counterpart is limited to
**manager review and follow-up status** for the **already collected**
`marketing.public_leads` data. It does not claim full registration,
trial/calendar booking, player creation, referral payouts, marketing consent
or workflow parity.

The current public enquiry capture foundation must already have written
consent and one of a phone or email, via `marketing.store_academy_lead`.
The new inbox never makes public leads accessible to the mobile app or to
unauthorized FC/other organizations.

## Implemented

- `GET /api/admin/crm/academy/leads?organization_id=lefteria_fc_academy&offset=0&limit=25&status=new`: manager/super_admin only, Academy grant and `marketing:read`, bounded pagination, optional status filter; private records, no-store response.
- `POST /api/admin/crm/academy/leads/{leadId}/status?organization_id=lefteria_fc_academy`: manager/super_admin `marketing:write`, expected status and next status required; only `new→contacted`, `new→lost` and `contacted→lost`. States `trial_booked` and `registered` can be read for historical compatibility but not invented without roster/calendar evidence.
- PostgreSQL `marketing.read_admin_academy_leads` and `marketing.update_admin_academy_lead_status` are owner/session/role locked `SECURITY DEFINER` functions with exact server-side permission checks. The admin and mobile runtime roles are denied raw marketing tables; the admin role receives only narrowly scoped function execution.
- Every real status transition emits immutable `marketing.lead_review_events` and the global admin security audit. Replays do not duplicate events; concurrent stale transitions get conflict errors.
- Greek branded admin CRM page with organization-specific content, bounded detail display, validated responses and explicit human-confirmed status buttons. The page never sends Twilio messages or registers a player.
- Isolated, transaction-rolled-back SQL acceptance scenarios and frontend source invariant tests. CI grants are installed after the existing admin-auth role is provisioned.

## Missing before describing CRM as complete

Full privacy-preserving Academy intake and consent versioning, scoped document
upload, player record creation, a booked actual trial in the shared calendar,
role-based case owners, assigned coaches, staff follow-up history, historical
Mongo collection import, GDPR retention/export, safe email/SMS workflow with
documented consent, delivery evidence and admin reports. No default bulk
marketing messages can be sent. Build/SQL integration and OpenAPI contract
generation must pass before merging this branch.

Operator setup for a disposable test DB:
`psql -X -v admin_auth_role=... -v app_role=... -f ops/postgres/academy-crm-admin-grants.sql`,
then `psql -X -f ops/postgres/academy-crm-inbox-checks.sql`. **Do not apply
these changes against a live database.**

## Go-live gate

This work closes only one **slice** of the CRM feature. The overall launch
remains blocked by `docs/implementation/go-live-gap-audit-2026-11-09.md`
and `ops/release/go-live-gates.json`, which were safely merged as PR #144.
