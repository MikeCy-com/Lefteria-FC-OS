\set ON_ERROR_STOP on
-- Operator-only activation AFTER disposable-DB verification.
-- Admin auth and mobile runtime roles must be distinct. This does not
-- expose private goals to delegated managers, coaches, guardians or players:
-- SQL functions independently require current super-admin authority.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_app_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.goal_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.goal_mobile_role',:'app_role',true);
DO $check$
DECLARE
  a text:=current_setting('lefteria.goal_admin_role');
  m text:=current_setting('lefteria.goal_mobile_role');
  fn oid;
BEGIN
  IF a=m OR a=current_user OR
    NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=a
      AND rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
  THEN RAISE EXCEPTION 'goal_admin_role_invalid'; END IF;
  FOREACH fn IN ARRAY ARRAY[
    'club.read_admin_player_development_goals(text,text,text)'::regprocedure::oid,
    'club.create_admin_player_development_goal(text,text,text,uuid,text,text,text,date,uuid)'::regprocedure::oid,
    'club.update_admin_player_development_goal(text,text,text,uuid,uuid,integer,integer,boolean,text)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS (SELECT 1 FROM pg_proc p WHERE p.oid=fn
      AND p.prosecdef
      AND NOT EXISTS(SELECT 1 FROM aclexplode(
        coalesce(p.proacl,acldefault('f',p.proowner)))
        WHERE grantee=0 AND privilege_type='EXECUTE'))
    THEN RAISE EXCEPTION 'goal_function_grants_unsafe'; END IF;
  END LOOP;
END $check$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_player_development_goals(text,text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.create_admin_player_development_goal(
  text,text,text,uuid,text,text,text,date,uuid) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.update_admin_player_development_goal(
  text,text,text,uuid,uuid,integer,integer,boolean,text) TO :"admin_auth_role";
DO $verify$
DECLARE
  a text:=current_setting('lefteria.goal_admin_role');
  m text:=current_setting('lefteria.goal_mobile_role');
  fn oid;
  tbl text;
BEGIN
  FOREACH fn IN ARRAY ARRAY[
    'club.read_admin_player_development_goals(text,text,text)'::regprocedure::oid,
    'club.create_admin_player_development_goal(text,text,text,uuid,text,text,text,date,uuid)'::regprocedure::oid,
    'club.update_admin_player_development_goal(text,text,text,uuid,uuid,integer,integer,boolean,text)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(a,fn,'EXECUTE')
      OR has_function_privilege(m,fn,'EXECUTE')
    THEN RAISE EXCEPTION 'development_goal_grant_boundary_failed'; END IF;
  END LOOP;
  FOREACH tbl IN ARRAY ARRAY[
    'club.player_development_goals','club.player_development_goal_events'
  ] LOOP
    IF has_table_privilege(a,tbl,'SELECT,INSERT,UPDATE,DELETE')
      OR has_table_privilege(m,tbl,'SELECT,INSERT,UPDATE,DELETE')
    THEN RAISE EXCEPTION 'development_goal_private_rows_exposed'; END IF;
  END LOOP;
END $verify$;
COMMIT;