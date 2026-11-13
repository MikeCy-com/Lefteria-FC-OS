\set ON_ERROR_STOP on
\if :{?admin_auth_role}
\else
  \echo 'admin_auth_role required'
  \quit 1
\endif
\if :{?app_role}
\else
  \echo 'app_role required'
  \quit 1
\endif
BEGIN;
DO $birthday_grant_preflight$
DECLARE v_admin text:=:'admin_auth_role';v_mobile text:=:'app_role';v_proc oid;
BEGIN
  IF v_admin=v_mobile OR current_database() IN ('postgres','template0','template1')
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=v_admin AND rolcanlogin
      AND NOT rolsuper AND NOT rolbypassrls AND NOT rolinherit)
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=v_mobile)
    OR to_regclass('club.player_birthday_card_preferences') IS NULL
    OR to_regclass('club.player_birthday_card_preference_events') IS NULL THEN
    RAISE EXCEPTION 'birthday_grant_preconditions_failed';
  END IF;
  FOREACH v_proc IN ARRAY ARRAY[
    'club.read_admin_player_birthday_preferences(text,text,text)'::regprocedure::oid,
    'club.write_admin_player_birthday_preferences(text,text,text,integer,boolean,text,text,text)'::regprocedure::oid
  ] LOOP
    IF NOT (SELECT prosecdef AND NOT EXISTS(
      SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
      FROM pg_proc WHERE oid=v_proc) THEN
      RAISE EXCEPTION 'birthday_function_not_hardened';
    END IF;
  END LOOP;
END $birthday_grant_preflight$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_player_birthday_preferences(
  text,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.write_admin_player_birthday_preferences(
  text,text,text,integer,boolean,text,text,text) TO :"admin_auth_role";
DO $birthday_grant_postflight$
DECLARE v_admin text:=:'admin_auth_role';v_mobile text:=:'app_role';
  v_proc oid;v_table oid;
BEGIN
  FOREACH v_proc IN ARRAY ARRAY[
    'club.read_admin_player_birthday_preferences(text,text,text)'::regprocedure::oid,
    'club.write_admin_player_birthday_preferences(text,text,text,integer,boolean,text,text,text)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(v_admin,v_proc,'EXECUTE')
      OR has_function_privilege(v_mobile,v_proc,'EXECUTE') THEN
      RAISE EXCEPTION 'birthday_function_exposure';
    END IF;
  END LOOP;
  IF has_function_privilege(v_admin,
    'club.require_player_birthday_manager(text,text,text)','EXECUTE')
    OR has_function_privilege(v_mobile,
    'club.require_player_birthday_manager(text,text,text)','EXECUTE')
  THEN RAISE EXCEPTION 'birthday_helper_exposure'; END IF;
  FOREACH v_table IN ARRAY ARRAY[
    'club.player_birthday_card_preferences'::regclass::oid,
    'club.player_birthday_card_preference_events'::regclass::oid
  ] LOOP
    IF NOT (SELECT relrowsecurity FROM pg_class WHERE oid=v_table)
      OR has_table_privilege(v_admin,v_table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
      OR has_table_privilege(v_mobile,v_table,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE')
    THEN RAISE EXCEPTION 'birthday_raw_table_exposure'; END IF;
  END LOOP;
END $birthday_grant_postflight$;
COMMIT;
