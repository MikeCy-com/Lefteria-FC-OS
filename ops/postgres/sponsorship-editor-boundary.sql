\set ON_ERROR_STOP on
\set ECHO none
-- Run AFTER the migration, public-sections-app-grants.sql and
-- web-admin-auth-grants.sql in the disposable staging database.
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'app_role_required'; END $required$;
\endif
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.sponsor_app_role',:'app_role',true),
       set_config('lefteria.sponsor_admin_role',:'admin_auth_role',true);
DO $audit$
DECLARE mobile text:=current_setting('lefteria.sponsor_app_role');
        admin text:=current_setting('lefteria.sponsor_admin_role');
BEGIN
  IF mobile=admin OR current_database() IN ('postgres','template0','template1')
    OR to_regclass('public_content.sponsorship_editorial') IS NULL
    OR NOT has_function_privilege(mobile,'public_content.read_published_sponsorship(text)','EXECUTE')
    OR has_function_privilege(mobile,'public_content.current_sponsorship_exists(text,text,text)','EXECUTE')
    OR has_table_privilege(mobile,'public_content.sponsorship_editorial','SELECT')
    OR has_column_privilege(mobile,'public_content.sponsorship_editorial','draft_json','SELECT')
    OR has_table_privilege(mobile,'public_content.sponsorship_editorial','INSERT')
    OR NOT has_function_privilege(admin,'public_content.current_sponsorship_exists(text,text,text)','EXECUTE')
    OR has_function_privilege(admin,'public_content.read_published_sponsorship(text)','EXECUTE')
    OR NOT has_table_privilege(admin,'public_content.sponsorship_editorial','SELECT')
    OR NOT has_column_privilege(admin,'public_content.sponsorship_editorial','draft_json','UPDATE')
  THEN RAISE EXCEPTION 'sponsorship_editorial_permission_boundary_failed'; END IF;
  IF jsonb_typeof(public_content.read_published_sponsorship('unknown_organization')) <> 'array'
     OR public_content.read_published_sponsorship('unknown_organization') <> '[]'::jsonb
     OR public_content.current_sponsorship_exists('unknown_organization','sponsor','one')
  THEN RAISE EXCEPTION 'sponsorship_unknown_owner_must_fail_closed'; END IF;
END $audit$;
ROLLBACK;
