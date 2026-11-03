\set ON_ERROR_STOP on
\set ECHO none
\set QUIET on

-- Administrator-only, after mobile bootstrap and the web-admin EF migration.
-- psql -X -v admin_auth_role=<auth-role> -v app_role=<mobile-role>
--      -f create-web-admin-auth-role.sql
-- Supply DATABASE_ADMIN_AUTH_PASSWORD and DATABASE_APP_PASSWORD only through
-- the process environment. No role password or attributes are ever rotated.
\if :{?admin_auth_role}
\else
  \echo 'admin_auth_role is required.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
\if :{?app_role}
\else
  \echo 'app_role must identify the existing separate mobile runtime role.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
\getenv admin_auth_password DATABASE_ADMIN_AUTH_PASSWORD
\getenv mobile_app_password DATABASE_APP_PASSWORD
\if :{?admin_auth_password}
\else
  \echo 'DATABASE_ADMIN_AUTH_PASSWORD is required.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
\if :{?mobile_app_password}
\else
  \echo 'DATABASE_APP_PASSWORD is required to check credential separation.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif

SELECT octet_length(:'admin_auth_role') BETWEEN 1 AND 63
       AND lower(:'admin_auth_role') <> 'public'
       AND lower(:'admin_auth_role') !~ '^pg_'
       AND :'admin_auth_role' <> :'app_role'
       AND :'admin_auth_role' <> current_user
       AND current_database() NOT IN ('postgres', 'template0', 'template1')
       AS admin_role_input_valid,
       length(:'admin_auth_password') >= 32
       AND length(:'mobile_app_password') > 0
       AND :'admin_auth_password' <> :'mobile_app_password'
       AS admin_password_input_valid,
       to_regclass('identity.admin_sessions') IS NOT NULL
       AND to_regclass('identity.admin_auth_rate_buckets') IS NOT NULL
       AND to_regclass('public_content.edits') IS NOT NULL
       AND to_regprocedure('identity.lock_admin_club_authority(text,text)') IS NOT NULL
       AND EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='audit' AND table_name='security_events' AND column_name='actor_realm')
       AND EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_schema='identity' AND table_name='admin_accounts' AND column_name='otp2fa_enabled')
       AS admin_schema_present
\gset
\if :admin_role_input_valid
\else
  \echo 'Use a distinct non-reserved auth role and a dedicated rewrite database.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
\if :admin_password_input_valid
\else
  \echo 'Use an admin-auth password of at least 32 characters, different from the mobile password.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif
\if :admin_schema_present
\else
  \echo 'Apply the web-admin EF migration first; this script never creates tables.'
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_operator_input_required'; END; $required$;
\endif

SELECT format(
  'CREATE ROLE %I LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
  :'admin_auth_role', :'admin_auth_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=:'admin_auth_role')
\gexec
\unset admin_auth_password
\unset mobile_app_password
\ir web-admin-auth-grants.sql
