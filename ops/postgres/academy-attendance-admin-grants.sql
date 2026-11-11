\set ON_ERROR_STOP on
\set ECHO none
-- Explicit operator-only setup. Never grant raw youth data or let the
-- public/mobile app invoke privileged training attendance operations.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_runtime_role_required'; END $required$;
\endif

BEGIN;
SELECT set_config('lefteria.attendance_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.attendance_mobile_role',:'app_role',true);

DO $boundary$
DECLARE
  v_admin text:=current_setting('lefteria.attendance_admin_role');
  v_mobile text:=current_setting('lefteria.attendance_mobile_role');
  v_oid oid;
BEGIN
  IF v_admin=v_mobile OR v_admin=current_user
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=v_admin
      AND rolcanlogin AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
    OR to_regclass('club.academy_training_attendance') IS NULL
    OR to_regclass('club.academy_training_attendance_events') IS NULL
  THEN RAISE EXCEPTION 'attendance_grant_preconditions_failed'; END IF;

  FOREACH v_oid IN ARRAY ARRAY[
    'club.read_admin_academy_training_sessions(text,text,text,date,date)'::regprocedure::oid,
    'club.read_admin_academy_training_attendance(text,text,text)'::regprocedure::oid,
    'club.record_admin_academy_training_attendance(text,text,text,text,text,integer)'::regprocedure::oid
  ] LOOP
    IF NOT (SELECT prosecdef AND prolang=(
        SELECT oid FROM pg_language WHERE lanname='plpgsql')
       AND NOT EXISTS(SELECT 1 FROM aclexplode(
          coalesce(proacl,acldefault('f',proowner)))
          WHERE grantee=0 AND privilege_type='EXECUTE')
       FROM pg_proc WHERE oid=v_oid)
    THEN RAISE EXCEPTION 'attendance_definer_not_hardened'; END IF;
  END LOOP;
END $boundary$;

GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_academy_training_sessions(
  text,text,text,date,date) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_academy_training_attendance(
  text,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.record_admin_academy_training_attendance(
  text,text,text,text,text,integer) TO :"admin_auth_role";

DO $verify$
DECLARE
  v_admin text:=current_setting('lefteria.attendance_admin_role');
  v_mobile text:=current_setting('lefteria.attendance_mobile_role');
  v_oid oid;
BEGIN
  FOREACH v_oid IN ARRAY ARRAY[
    'club.read_admin_academy_training_sessions(text,text,text,date,date)'::regprocedure::oid,
    'club.read_admin_academy_training_attendance(text,text,text)'::regprocedure::oid,
    'club.record_admin_academy_training_attendance(text,text,text,text,text,integer)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(v_admin,v_oid,'EXECUTE')
      OR has_function_privilege(v_mobile,v_oid,'EXECUTE')
    THEN RAISE EXCEPTION 'attendance_execute_grant_invalid'; END IF;
  END LOOP;
  IF has_function_privilege(v_admin,
       'club.academy_training_player_eligible(text,text,text)','EXECUTE')
     OR has_function_privilege(v_mobile,
       'club.academy_training_player_eligible(text,text,text)','EXECUTE')
  THEN RAISE EXCEPTION 'attendance_private_eligibility_helper_exposed'; END IF;
  IF EXISTS (
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='club' AND c.relname IN
      ('academy_training_attendance','academy_training_attendance_events')
      AND (
        has_any_column_privilege(v_admin,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(v_admin,c.oid,'DELETE,TRUNCATE,TRIGGER')
        OR has_any_column_privilege(v_mobile,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
        OR has_table_privilege(v_mobile,c.oid,'DELETE,TRUNCATE,TRIGGER')
      )
  ) THEN
    RAISE EXCEPTION 'attendance_raw_youth_data_exposed';
  END IF;
END $verify$;
COMMIT;
