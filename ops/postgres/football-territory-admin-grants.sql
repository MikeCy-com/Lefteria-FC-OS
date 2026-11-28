\set ON_ERROR_STOP on
-- OPERATOR-APPROVED, for the dedicated WebAdmin login and mobile app roles.
-- Apply only after the territory admin migration and web-admin auth bootstrap.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'football_admin_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'football_mobile_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.football_admin_role', :'admin_auth_role', true),
       set_config('lefteria.football_mobile_role', :'app_role', true);
DO $guard$
DECLARE
  a text:=current_setting('lefteria.football_admin_role');
  m text:=current_setting('lefteria.football_mobile_role');
  f oid;
  c record;
BEGIN
  IF a=m OR a=current_user OR m=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=a
      AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolinherit)
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=m AND NOT rolsuper)
  THEN RAISE EXCEPTION 'football_admin_roles_invalid'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'club.authorize_admin_football(text,text)'::regprocedure::oid,
    'club.admin_football_allowed(uuid,text,text,uuid,uuid,boolean)'::regprocedure::oid,
    'club.read_admin_football_directory(text,text)'::regprocedure::oid,
    'club.create_admin_football_resource(text,text,text,jsonb)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=f
      AND p.prosecdef AND p.proowner=(
        SELECT relowner FROM pg_class WHERE oid='club.football_cities'::regclass)
      AND EXISTS(SELECT 1 FROM unnest(coalesce(p.proconfig,ARRAY[]::text[])) AS conf(entry)
        WHERE regexp_replace(conf.entry,'[[:space:]]','','g')='search_path=pg_catalog,pg_temp'))
      OR EXISTS(SELECT 1 FROM pg_proc p,
        LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE p.oid=f AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
    THEN RAISE EXCEPTION 'football_admin_function_unprotected'; END IF;
  END LOOP;
  FOR c IN SELECT unnest(ARRAY[
    'football_cities','football_locations','football_operational_scopes',
    'academy_group_location_assignments','football_opponents',
    'football_playing_fields','football_training_bases','football_match_drafts',
    'football_city_admin_grants','football_location_admin_grants',
    'football_team_admin_grants']) AS name
  LOOP
    IF has_any_column_privilege(a,format('club.%I',c.name),'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(a,format('club.%I',c.name),'DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(m,format('club.%I',c.name),'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(m,format('club.%I',c.name),'DELETE,TRUNCATE,TRIGGER')
    THEN RAISE EXCEPTION 'football_catalog_direct_table_privilege'; END IF;
  END LOOP;
END $guard$;

GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_football_directory(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.create_admin_football_resource(text,text,text,jsonb)
  TO :"admin_auth_role";
-- Helpers always execute within the owner-definer SQL; never grant their
-- callable signatures or private table SELECT to a browser-facing role.
REVOKE ALL ON FUNCTION club.authorize_admin_football(text,text),
  club.admin_football_allowed(uuid,text,text,uuid,uuid,boolean)
  FROM :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.read_admin_football_directory(text,text),
  club.create_admin_football_resource(text,text,text,jsonb)
  FROM :"app_role";
-- Exact per-team settings, never direct access to private configuration rows.
REVOKE ALL ON TABLE club.group_match_timing FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.require_admin_match_timing_host(text,text,text),club.freeze_match_timing()
  FROM PUBLIC, :"admin_auth_role", :"app_role";
REVOKE ALL ON FUNCTION club.read_admin_group_match_timing(text,text,text),
  club.write_admin_group_match_timing(text,text,text,integer,integer,integer)
  FROM PUBLIC, :"app_role", :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_group_match_timing(text,text,text),
  club.write_admin_group_match_timing(text,text,text,integer,integer,integer) TO :"admin_auth_role";
COMMIT;
