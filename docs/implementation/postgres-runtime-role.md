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
- Admin accounts are SELECT-only except UPDATE of active_organization_id.
- Mobile identities permit SELECT, fan-only INSERT, and UPDATE of profile,
  platform and last-login fields. ID, phone, role, active status, legacy ID and
  management linkage cannot be updated by the runtime role.
- OTPs permit SELECT/INSERT/UPDATE/DELETE. Throttle rows permit SELECT/INSERT
  and UPDATE of token/time state.
- Security audit permits INSERT only: no SELECT, UPDATE, DELETE or TRUNCATE.
- No sequence or migration-history privileges are granted.

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
