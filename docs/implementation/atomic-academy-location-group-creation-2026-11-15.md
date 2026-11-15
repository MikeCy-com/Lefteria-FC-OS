# Atomic Academy location group creation and legacy flat-group restriction

This slice moves group creation toward the operational hierarchy required
by Lefteria FC OS: First Team squads are isolated; Academy cities share
opponent and playing-field catalogs, while Academy **locations** own their
groups, staff permissions, training bases, finances and messaging audiences.

## Transaction boundary

`POST /api/admin/football/groups/with-profile?organization_id=lefteria_fc_academy`
now requires `location_id` as a concrete UUID. The creation form fetches
the current `/api/admin/football/territories` directory, lists only
active Academy locations where `can_manage=true`, and refuses missing
or ambiguous locations. URL parameters are only **hints** and cannot
grant access.

The SQL function
`club.create_admin_academy_location_group_with_profile(text,text,text,uuid,jsonb,uuid)`
locks current session/account/organization, resolves a canonical active
city+location+operational scope and invokes
`club.admin_football_allowed(...,true)` for that **exact site**. It then
atomically commits a request-id hash including location, one canonical
Academy group, its single city/location assignment, complete validated
original profile and audit. Any failure rolls all of these back.
Concurrent requests are serialized against the 150-group limit; duplicate
case-insensitive group names in the **same location** fail, but the same
age-group name in a different city/site is allowed.

Retries only replay a matching input hash for the same actor, original
location and valid current grant. Revoking site permission also revokes
retry/readback access. The role is not inferred from the URL and no
roster assignments, messages or billing records are written.

## Legacy compatibility and role safety

The old flat `club.create_admin_group` can no longer be used by a
manager who possesses only an organization grant; it is **Super Admin
only** for controlled legacy/onboarding recovery. The old profiled
Academy path is fail-closed without a site; First Team profiled group
creation is Super Admin only and automatically creates the corresponding
canonical First Team operational scope in the same transaction.

The UI shows a First Team create form only to Super Admin. The Academy
create form is available to managers with a valid verified location and
requires the explicit location selector; upon success it identifies the
actual assigned city/site instead of falsely prompting for a second
assignment.

Existing territory placement of **legacy** Academy groups remains
available to Super Admin separately for migration/reconciliation.

## Verification and limitations

- Real disposable PostgreSQL HTTP checks: authorized site manager success,
  same-city sibling location denial, no-grant manager denial, legacy
  flat-create denial, exact idempotent replay, changed-payload replay denial,
  same-site name conflict, audit rollback, immediate grant revocation.
- Existing CI: C# build, SQL migration apply/rollback/reapply, role grants,
  mobile identity PostgreSQL, Svelte compile/SSR, API/OpenAPI smoke,
  Docker Compose and browser smoke.
- A separate future slice should enforce the same exact site-level
  permissions for **editing existing** player/group profiles and
  assigning coaches; older profile-edit readers/writers currently use
  organization-level checks. Do not enable broader profile moderation
  or claim full city/location authorization until those operations
  are upgraded.
- Never deploy automatically to live Hostinger; this is source control
  work only.
