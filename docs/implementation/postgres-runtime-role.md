# PostgreSQL runtime role

The schema migration owner and the API runtime use different database roles.
Run schema migrations with the owner first. Then an administrator runs
ops/postgres/create-app-role.sql with the runtime role name supplied as psql
variable app_role and a strong password supplied through DATABASE_APP_PASSWORD.
The script reads the password with psql getenv, never echoes or resets it, and
creates the role only when absent. Secret rotation is a separate operator action.

This setup is for a database dedicated to the rewrite or disposable CI.
It refuses postgres/template databases and refuses a runtime role that owns a
database, user schema or persistent relation, has administrative capabilities,
or belongs to any other role. It does not silently change those role attributes.

Example with administrator PGHOST/PGPORT/PGUSER/PGDATABASE/PGPASSWORD configured
in the process environment and DATABASE_APP_PASSWORD obtained securely:

    psql -X -v app_role=chosen_runtime_role -f ops/postgres/create-app-role.sql

Do not put the password in a command argument, source file or build log.
The script includes identity-app-grants.sql relative to its own directory.
Compose can mount this directory read-only for its administrator setup command.
Apply it after every schema recreate or identity-table rollback/reapply.
After `MobileProfileDetails`, reapply it before starting the updated API: the
runtime also needs column UPDATE for email, favourite_player_id and updated_at.
Identity, phone, role, active status and management linkage remain excluded.

## Privilege boundary

- The API has database CONNECT and schema USAGE, without CREATE or TEMP.
- The dedicated database loses PUBLIC CREATE/TEMP, and PUBLIC loses CREATE on
  the relevant schemas. These are database-wide hardening changes; do not run
  this setup against a shared database without a separate reviewed plan.
- Organizations and organization grants are SELECT-only.
- Admin accounts expose only ID, active/invitation state, normalized phone and
  selected organization, plus UPDATE of active_organization_id. Credential hashes
  and full admin rows are denied to the mobile API runtime.
- Mobile identities permit SELECT, fan-only INSERT, and UPDATE of profile,
  platform and last-login fields. ID, phone, role, active status, legacy ID and
  management linkage cannot be updated by the runtime role.
- OTPs permit SELECT/INSERT/UPDATE/DELETE. Throttle rows permit SELECT/INSERT
  and UPDATE of token/time state.
- Security audit permits INSERT only: no SELECT, UPDATE, DELETE or TRUNCATE.
- No sequence or migration-history privileges are granted.
- Mobile avatars allow SELECT/INSERT and UPDATE of filename/JPEG/dimensions/time
  only. Ownership/provenance, DELETE and TRUNCATE are denied. Identity deletion
  cascades and replacements overwrite one current row per identity.
- Canonical roster access is limited to SELECT on the five player-picker fields,
  their eligibility predicates, team ID, player/group membership IDs and the
  import-readiness ID. Calendar scope also reads only team/group IDs and owning
  organization IDs. Whole player rows and roster writes are denied. The runtime has no
  migration-schema USAGE or access to exact source archives.
- Staff status, staff/team/group assignment IDs and group-coach links expose only
  the columns required to recheck current canonical mobile authority. Canonical
  contact bindings, mobile record links, admin/staff links and fan-registration
  evidence have narrow read projections; source IDs, raw fields and credentials
  remain excluded where unnecessary. Canonical writes are denied. Staff/account
  import state, full source, quarantine, subject mappings, grant provenance and
  original player/guardian/staff contacts remain owner-only. The import executable
  requires its own explicit owner connection; it is not part of API DI.

Reapply grants after `LegacyAccountImport` before starting the new API. The
reset includes new account tables and denies residual table/column privileges.
Runtime probes exercise whole-row/source/hash access and canonical write denials.

After `LegacyMobileBridge`, reapply grants for the two-column
`identity.imported_mobile_subjects` view. It uses owner permissions and a security
barrier to expose exact imported mobile subject/target pairs; it grants no access
to original migration mappings or source objects. Runtime view writes remain
denied. View-only rollback/reapply preserves populated account data and needs
the view's SELECT grants reapplied. See [bridge recovery](legacy-mobile-bridge.md).

After `ScopedCalendar`, reapply grants before starting the updated API. Calendar
import state exposes only `id,time_zone,is_complete`; full manifest bytes and
metadata remain owner-only. The runtime reads curated item/scope/guest/response
tables and may insert/update only RSVP status, actor and time with the permitted
keys on insert. Response keys and imported source ordinals cannot be updated.
Restrictive RLS limits INSERT/UPDATE to `availability`/`event_attendance`, valid
RSVP statuses and a non-null actor; physical `attendance` rows remain protected.
DELETE/TRUNCATE, calendar canonical writes, raw source/quarantine and readiness
mutation are denied. Application SQL still enforces each signed identity's
current child/staff/guest/organization scope. Its audit INSERT returns a constant
and does not require audit SELECT. See [calendar recovery](scoped-calendar.md).

Mobile identity RLS restricts INSERT to fan records. The fan check is restrictive,
so an additional permissive insert policy cannot silently broaden it. SELECT
and UPDATE policies let the repository evaluate canonical phone and identity
queries; application authorization still scopes every request. Runtime grants
exclude role/link/status updates. Formal roles are provisioned by controlled
owner import/administration, which retains the normal table-owner RLS bypass.

The policies bind one approved API role. Re-running with a different role
replaces those policy targets; coordinate any role-name rotation with the
deployment. The script resets direct and PUBLIC table/column grants in this
module, removes direct CREATE on public, and refuses to commit if the runtime
role can CREATE in any unexpected user schema. Review that schema's grants
separately before retrying.

PostgreSQL row-locking SELECT requires UPDATE on at least one selected column.
The API has sufficient column UPDATE for identity/admin row locks. It must not
use FOR SHARE/FOR UPDATE on SELECT-only organization grants; selected-context
integrity is protected by the admin row lock and composite active-assignment FK.

## Verification

Connect to a disposable migrated database as the runtime role and run:

    psql -X -f ops/postgres/identity-app-permissions-check.sql

The transaction proves allowed fan/profile/audit INSERT operations and rejects
formal-role insertion, identity-role mutation, admin-role mutation, grant mutation,
audit reads/updates/deletes/truncate, and DDL privileges. Probe writes are rolled
back. Run the authenticated API integration suite with the same runtime
connection; use the owner connection only for migrations and deliberate seeding.
