\set ON_ERROR_STOP on
-- Dedicated, disposable-first grant script. Staff may call only audited
-- per-player functions; app/mobile cannot see youth portraits or profiles.
\if :{?admin_auth_role}
\else
  \echo 'admin_auth_role is required'
  \quit 2
\endif
\if :{?app_role}
\else
  \echo 'app_role is required'
  \quit 2
\endif
BEGIN;
SELECT set_config('lefteria.player_profiles_admin_role', :'admin_auth_role',true);
SELECT set_config('lefteria.player_profiles_app_role', :'app_role',true);
DO $guard$
DECLARE a text:=current_setting('lefteria.player_profiles_admin_role');
  m text:=current_setting('lefteria.player_profiles_app_role');
  fn oid;
BEGIN
  IF a=m OR NOT EXISTS(
      SELECT 1 FROM pg_roles WHERE rolname=a AND rolcanlogin AND NOT rolsuper
      AND NOT rolbypassrls AND NOT rolinherit)
     OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=m AND NOT rolsuper)
     OR to_regclass('club.academy_player_private_profiles') IS NULL
  THEN RAISE EXCEPTION 'academy_profile_runtime_role_invalid'; END IF;
  FOREACH fn IN ARRAY ARRAY[
    'club.admin_academy_player_lifecycle(text,text,text,integer,uuid,text)'::regprocedure::oid,
    'club.authorize_academy_private_profile(text,text,boolean)'::regprocedure::oid,
    'club.read_admin_academy_player_profile(text,text)'::regprocedure::oid,
    'club.write_admin_academy_player_profile(text,text,integer,text,text,integer)'::regprocedure::oid,
    'club.write_admin_academy_player_contact(text,text,integer,text,text,text,text)'::regprocedure::oid,
    'club.rename_admin_academy_player(text,text,text,text)'::regprocedure::oid,
    'club.write_admin_academy_player_sporting(text,text,text,integer,text,integer)'::regprocedure::oid,
    'club.read_admin_academy_player_portrait(text,text)'::regprocedure::oid,
    'club.write_admin_academy_player_portrait(text,text,bytea)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=fn AND p.prosecdef
      AND EXISTS(SELECT 1 FROM unnest(coalesce(p.proconfig,ARRAY[]::text[])) c(entry)
        WHERE regexp_replace(c.entry,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp'))
    THEN RAISE EXCEPTION 'academy_profile_function_unprotected'; END IF;
  END LOOP;
END $guard$;
REVOKE ALL ON club.academy_player_private_profiles, club.academy_player_contact_details, academy_intake.registration_sporting_baselines
  FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION
  club.authorize_academy_private_profile(text,text,boolean),
  club.read_admin_academy_player_profile(text,text),
  club.write_admin_academy_player_profile(text,text,integer,text,text,integer),
  club.write_admin_academy_player_contact(text,text,integer,text,text,text,text),
  club.rename_admin_academy_player(text,text,text,text),
  club.write_admin_academy_player_sporting(text,text,text,integer,text,integer),
  club.read_admin_academy_player_portrait(text,text),
  club.write_admin_academy_player_portrait(text,text,bytea)
  FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON club.academy_player_lifecycle_events FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.admin_academy_player_lifecycle(text,text,text,integer,uuid,text) FROM PUBLIC, :"app_role";
GRANT EXECUTE ON FUNCTION club.admin_academy_player_lifecycle(text,text,text,integer,uuid,text) TO :"admin_auth_role";
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_academy_player_profile(text,text),
  club.write_admin_academy_player_profile(text,text,integer,text,text,integer),
  club.write_admin_academy_player_contact(text,text,integer,text,text,text,text),
  club.rename_admin_academy_player(text,text,text,text),
  club.write_admin_academy_player_sporting(text,text,text,integer,text,integer),
  club.read_admin_academy_player_portrait(text,text),
  club.write_admin_academy_player_portrait(text,text,bytea)
  TO :"admin_auth_role";

DO $verify$
DECLARE a text:=current_setting('lefteria.player_profiles_admin_role');
  m text:=current_setting('lefteria.player_profiles_app_role');
BEGIN
  IF has_table_privilege(a,'club.academy_player_lifecycle_events','SELECT,INSERT,UPDATE,DELETE')
    OR has_table_privilege(m,'club.academy_player_lifecycle_events','SELECT,INSERT,UPDATE,DELETE')
    OR has_function_privilege(m,'club.admin_academy_player_lifecycle(text,text,text,integer,uuid,text)','EXECUTE')
    OR NOT has_function_privilege(a,'club.admin_academy_player_lifecycle(text,text,text,integer,uuid,text)','EXECUTE')
    OR has_any_column_privilege(a,'club.academy_player_private_profiles',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(a,'club.academy_player_private_profiles','DELETE')
    OR has_any_column_privilege(m,'club.academy_player_private_profiles',
      'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(m,'club.academy_player_private_profiles','DELETE')
    OR has_any_column_privilege(a,'club.academy_player_contact_details','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(a,'club.academy_player_contact_details','DELETE')
    OR has_any_column_privilege(m,'club.academy_player_contact_details','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(m,'club.academy_player_contact_details','DELETE')
    OR has_function_privilege(m,'club.write_admin_academy_player_contact(text,text,integer,text,text,text,text)','EXECUTE')
    OR NOT has_function_privilege(a,'club.write_admin_academy_player_contact(text,text,integer,text,text,text,text)','EXECUTE')
    OR has_function_privilege(m,'club.rename_admin_academy_player(text,text,text,text)','EXECUTE')
    OR NOT has_function_privilege(a,'club.rename_admin_academy_player(text,text,text,text)','EXECUTE')
    OR has_function_privilege(m,'club.write_admin_academy_player_sporting(text,text,text,integer,text,integer)','EXECUTE')
    OR NOT has_function_privilege(a,'club.write_admin_academy_player_sporting(text,text,text,integer,text,integer)','EXECUTE')
    OR has_any_column_privilege(a,'academy_intake.registration_sporting_baselines','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_any_column_privilege(m,'academy_intake.registration_sporting_baselines','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(a,'academy_intake.registration_sporting_baselines','DELETE,TRUNCATE,TRIGGER')
    OR has_table_privilege(m,'academy_intake.registration_sporting_baselines','DELETE,TRUNCATE,TRIGGER')
    OR has_function_privilege(a,
      'club.authorize_academy_private_profile(text,text,boolean)','EXECUTE')
    OR has_function_privilege(m,
      'club.read_admin_academy_player_profile(text,text)','EXECUTE')
    OR has_function_privilege(m,
      'club.read_admin_academy_player_portrait(text,text)','EXECUTE')
    OR has_function_privilege(m,
      'club.write_admin_academy_player_portrait(text,text,bytea)','EXECUTE')
    OR has_function_privilege(m,
      'club.write_admin_academy_player_profile(text,text,integer,text,text,integer)','EXECUTE')
  THEN RAISE EXCEPTION 'academy_private_profile_boundary_broken'; END IF;
END $verify$;
COMMIT;
