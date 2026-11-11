\set ON_ERROR_STOP on
\set ECHO none
-- Run only after the migration and the existing dedicated web-admin auth role.
-- Required psql args: -v admin_auth_role=... -v app_role=...
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_app_role_required'; END $required$;
\endif

BEGIN;
SELECT set_config('lefteria.crm_admin_auth_role',:'admin_auth_role',true),
       set_config('lefteria.crm_mobile_role',:'app_role',true);

DO $guard$
DECLARE
  v_admin text:=current_setting('lefteria.crm_admin_auth_role');
  v_mobile text:=current_setting('lefteria.crm_mobile_role');
BEGIN
  IF v_admin=v_mobile OR v_admin=current_user OR
    v_admin NOT IN (SELECT rolname FROM pg_roles WHERE rolcanlogin AND NOT rolinherit
      AND NOT rolsuper AND NOT rolbypassrls)
    OR to_regprocedure('marketing.read_admin_academy_leads(text,text,integer,integer,text)') IS NULL
    OR to_regprocedure('marketing.update_admin_academy_lead_status(text,text,uuid,text,text)') IS NULL
    OR to_regclass('marketing.lead_review_events') IS NULL THEN
    RAISE EXCEPTION 'academy_crm_admin_grant_not_safe';
  END IF;
END $guard$;

GRANT USAGE ON SCHEMA marketing TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION marketing.read_admin_academy_leads(
  text,text,integer,integer,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION marketing.update_admin_academy_lead_status(
  text,text,uuid,text,text) TO :"admin_auth_role";

DO $check$
DECLARE v_admin text:=current_setting('lefteria.crm_admin_auth_role');
        v_mobile text:=current_setting('lefteria.crm_mobile_role');
        v_oid oid;
BEGIN
  FOREACH v_oid IN ARRAY ARRAY[
    'marketing.read_admin_academy_leads(text,text,integer,integer,text)'::regprocedure::oid,
    'marketing.update_admin_academy_lead_status(text,text,uuid,text,text)'::regprocedure::oid
  ] LOOP
    IF NOT (SELECT prosecdef AND provolatile='v'
      AND prolang=(SELECT oid FROM pg_language WHERE lanname='plpgsql')
      AND NOT EXISTS(SELECT 1 FROM aclexplode(COALESCE(proacl,acldefault('f',proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE')
      FROM pg_proc WHERE oid=v_oid)
      OR NOT has_function_privilege(v_admin,v_oid,'EXECUTE')
      OR has_function_privilege(v_mobile,v_oid,'EXECUTE') THEN
      RAISE EXCEPTION 'crm_routine_exposes_unreviewed_privileges';
    END IF;
  END LOOP;
  IF EXISTS (
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='marketing' AND c.relname IN ('public_leads','lead_events','lead_review_events')
      AND (
        has_any_column_privilege(v_admin,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(v_admin,c.oid,'DELETE,TRUNCATE,TRIGGER')
        OR has_any_column_privilege(v_mobile,c.oid,'INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(v_mobile,c.oid,'DELETE,TRUNCATE,TRIGGER')
      )
  ) THEN
    RAISE EXCEPTION 'crm_raw_table_grant_forbidden';
  END IF;
END $check$;
COMMIT;
