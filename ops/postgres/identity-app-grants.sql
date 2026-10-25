\set ON_ERROR_STOP on
\set ECHO none

-- ADMINISTRATOR-ONLY. The current database must be dedicated to this rewrite.
-- This revokes PUBLIC temporary/create rights within that database and binds the
-- runtime policies to app_role. Apply after migrations, never as the API role.
\if :{?app_role}
\else
  \echo 'app_role is required.'
  \quit 2
\endif

SELECT current_database() AS identity_database
\gset
BEGIN;
SELECT set_config('lefteria.runtime_role', :'app_role', true)
\gset

DO $guard$
DECLARE
  runtime_name text := current_setting('lefteria.runtime_role');
  runtime_role pg_roles%ROWTYPE;
BEGIN
  IF current_database() IN ('postgres', 'template0', 'template1') THEN
    RAISE EXCEPTION 'Identity grants require a dedicated application database';
  END IF;
  SELECT * INTO runtime_role FROM pg_roles WHERE rolname = runtime_name;
  IF NOT FOUND THEN
    RAISE EXCEPTION 'Runtime role must be created by an administrator first';
  END IF;
  IF runtime_role.rolsuper OR runtime_role.rolcreatedb OR runtime_role.rolcreaterole
      OR runtime_role.rolreplication OR runtime_role.rolbypassrls THEN
    RAISE EXCEPTION 'Refusing a privileged runtime role; no role attributes were changed';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_auth_members WHERE member = runtime_role.oid) THEN
    RAISE EXCEPTION 'Runtime role must have no role memberships, including SET ROLE paths';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_database WHERE datdba = runtime_role.oid)
      OR EXISTS (SELECT 1 FROM pg_namespace WHERE nspowner = runtime_role.oid
                   AND nspname NOT LIKE 'pg_%')
      OR EXISTS (SELECT 1 FROM pg_class WHERE relowner = runtime_role.oid
                   AND relpersistence <> 't') THEN
    RAISE EXCEPTION 'Runtime role must not own a database, schema or persistent relation';
  END IF;
  IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
             WHERE n.nspname = 'identity' AND c.relname = 'mobile_identities'
               AND c.relforcerowsecurity) THEN
    RAISE EXCEPTION 'Review owner import behavior before applying to a FORCE RLS table';
  END IF;
END
$guard$;

-- PUBLIC privileges are inherited even by NOINHERIT roles; per-role REVOKE
-- cannot cancel them. These changes are scoped to the dedicated current DB.
REVOKE CREATE, TEMPORARY ON DATABASE :"identity_database" FROM PUBLIC;
REVOKE CREATE ON SCHEMA public, identity, club, audit FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM :"app_role";
REVOKE ALL PRIVILEGES ON DATABASE :"identity_database" FROM :"app_role";
GRANT CONNECT ON DATABASE :"identity_database" TO :"app_role";
REVOKE ALL PRIVILEGES ON SCHEMA identity, club, audit FROM :"app_role";
GRANT USAGE ON SCHEMA identity, club, audit TO :"app_role";

REVOKE ALL PRIVILEGES ON TABLE
  identity.mobile_identities, identity.admin_accounts,
  identity.admin_organization_grants, identity.otp_challenges,
  identity.auth_rate_buckets, club.organizations, audit.security_events
FROM PUBLIC, :"app_role";

-- Also remove prior column grants, which a table-level REVOKE does not remove.
DO $columns$
DECLARE
  target record;
  column_names text;
BEGIN
  FOR target IN
    SELECT n.nspname, c.relname, c.oid
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE (n.nspname, c.relname) IN (
      ('identity','mobile_identities'), ('identity','admin_accounts'),
      ('identity','admin_organization_grants'), ('identity','otp_challenges'),
      ('identity','auth_rate_buckets'), ('club','organizations'), ('audit','security_events'))
  LOOP
    SELECT string_agg(format('%I', a.attname), ', ' ORDER BY a.attnum)
      INTO column_names
      FROM pg_attribute a WHERE a.attrelid = target.oid AND a.attnum > 0 AND NOT a.attisdropped;
    EXECUTE format('REVOKE ALL PRIVILEGES (%s) ON TABLE %I.%I FROM PUBLIC, %I',
      column_names, target.nspname, target.relname, current_setting('lefteria.runtime_role'));
  END LOOP;
END
$columns$;

GRANT SELECT, INSERT ON identity.mobile_identities TO :"app_role";
GRANT UPDATE (display_name, first_name, last_name, birthday, avatar_url,
  favorite_team, notification_preferences, platform, last_login)
ON identity.mobile_identities TO :"app_role";
GRANT SELECT ON identity.admin_accounts, identity.admin_organization_grants,
  club.organizations TO :"app_role";
GRANT UPDATE (active_organization_id) ON identity.admin_accounts TO :"app_role";
GRANT SELECT, INSERT, UPDATE, DELETE ON identity.otp_challenges TO :"app_role";
GRANT SELECT, INSERT ON identity.auth_rate_buckets TO :"app_role";
GRANT UPDATE (tokens, updated_at) ON identity.auth_rate_buckets TO :"app_role";
GRANT INSERT ON audit.security_events TO :"app_role";

-- The API can create verified fan records, never formal club-role grants.
-- Owner migrations/imports retain the normal table-owner RLS bypass.
ALTER TABLE identity.mobile_identities ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS mobile_runtime_select ON identity.mobile_identities;
DROP POLICY IF EXISTS mobile_runtime_insert ON identity.mobile_identities;
DROP POLICY IF EXISTS mobile_runtime_insert_fan_only ON identity.mobile_identities;
DROP POLICY IF EXISTS mobile_runtime_update ON identity.mobile_identities;
CREATE POLICY mobile_runtime_select ON identity.mobile_identities
  FOR SELECT TO :"app_role" USING (true);
CREATE POLICY mobile_runtime_insert ON identity.mobile_identities
  FOR INSERT TO :"app_role" WITH CHECK (true);
CREATE POLICY mobile_runtime_insert_fan_only ON identity.mobile_identities
  AS RESTRICTIVE FOR INSERT TO :"app_role" WITH CHECK (role = 'fan');
CREATE POLICY mobile_runtime_update ON identity.mobile_identities
  FOR UPDATE TO :"app_role" USING (true) WITH CHECK (true);

-- A dedicated database should have no unrelated schema grants. Refuse rather
-- than silently changing permissions on an unexpected application schema.
DO $ddl_guard$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_namespace n
             WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
               AND has_schema_privilege(current_setting('lefteria.runtime_role'), n.oid, 'CREATE')) THEN
    RAISE EXCEPTION 'Runtime role retains CREATE on an unexpected schema; review its grants';
  END IF;
END
$ddl_guard$;
COMMIT;
