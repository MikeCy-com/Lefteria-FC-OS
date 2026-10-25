\set ON_ERROR_STOP on
\set ECHO none
\set QUIET on

-- Administrator-only setup for a dedicated rewrite database, after EF migrations.
-- Invocation: psql -X -v app_role=<runtime-role> -f create-app-role.sql
-- DATABASE_APP_PASSWORD must be supplied through the process environment.
\if :{?app_role}
\else
  \echo 'app_role is required.'
  \quit 2
\endif
\getenv app_password DATABASE_APP_PASSWORD
\if :{?app_password}
\else
  \echo 'DATABASE_APP_PASSWORD is required.'
  \quit 2
\endif

SELECT length(:'app_role') BETWEEN 1 AND 63
       AND lower(:'app_role') <> 'public'
       AND :'app_role' !~ '^pg_'
       AND current_database() NOT IN ('postgres', 'template0', 'template1')
       AS role_input_valid,
       length(:'app_password') >= 32 AS password_input_valid
\gset
\if :role_input_valid
\else
  \echo 'Use a non-reserved runtime role and a dedicated application database.'
  \quit 2
\endif
\if :password_input_valid
\else
  \echo 'DATABASE_APP_PASSWORD must contain at least 32 characters.'
  \quit 2
\endif

-- A pre-existing role is never silently weakened or given a new password.
-- identity-app-grants.sql validates its ownership, capabilities and memberships.
SELECT format(
  'CREATE ROLE %I LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD %L',
  :'app_role', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'app_role')
\gexec
\unset app_password
\ir identity-app-grants.sql
