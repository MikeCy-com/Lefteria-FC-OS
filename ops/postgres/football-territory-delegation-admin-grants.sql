\set ON_ERROR_STOP on
\set ECHO none
-- APPROVED OPERATOR ONLY; run AFTER EF migrations and the initial
-- football-territory-admin-grants.sql in isolated OS staging.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'football_delegation_admin_role_missing'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'football_delegation_mobile_role_missing'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.football_delegation_admin_role',:'admin_auth_role',true),
       set_config('lefteria.football_delegation_mobile_role',:'app_role',true);
DO $guard$
DECLARE
  a text:=current_setting('lefteria.football_delegation_admin_role');
  m text:=current_setting('lefteria.football_delegation_mobile_role');
  f oid;
BEGIN
  IF a=m OR a=current_user OR m=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=a
      AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolinherit)
  THEN RAISE EXCEPTION 'football_delegation_invalid_credentials'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'club.read_admin_football_access(text,text)'::regprocedure::oid,
    'club.write_admin_football_access(text,text,uuid,text,uuid,text,boolean)'::regprocedure::oid,
    'club.read_admin_football_location_metrics(text,text)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(
      SELECT 1 FROM pg_proc p
      WHERE p.oid=f AND p.prosecdef
        AND p.proowner=(SELECT relowner FROM pg_class
          WHERE oid='club.football_cities'::regclass)
        AND EXISTS(SELECT 1 FROM unnest(coalesce(p.proconfig,ARRAY[]::text[]))
          AS conf(entry) WHERE regexp_replace(conf.entry,'[[:space:]]','','g')
            ='search_path=pg_catalog,pg_temp')
    ) OR EXISTS (
      SELECT 1 FROM pg_proc p,
        LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
      WHERE p.oid=f AND acl.grantee=0 AND acl.privilege_type='EXECUTE'
    )
    THEN RAISE EXCEPTION 'football_delegation_unprotected_function'; END IF;
  END LOOP;
  -- The mobile app legitimately reads players/group memberships behind RLS;
  -- never revoke these existing privileges. Enforce full denial for all
  -- newly introduced territory tables, and the admin login for every table.
  IF EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='club' AND c.relname IN (
      'football_city_admin_grants','football_location_admin_grants',
      'football_team_admin_grants','academy_group_location_assignments',
      'players','player_academy_groups','football_locations')
    AND (has_any_column_privilege(a,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(a,c.oid,'DELETE,TRUNCATE,TRIGGER')
      OR (c.relname NOT IN ('players','player_academy_groups')
        AND (has_any_column_privilege(m,c.oid,'SELECT,INSERT,UPDATE,REFERENCES')
          OR has_table_privilege(m,c.oid,'DELETE,TRUNCATE,TRIGGER')))))
  THEN RAISE EXCEPTION 'football_delegation_direct_table_access'; END IF;
END $guard$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION
  club.read_admin_football_access(text,text),
  club.write_admin_football_access(text,text,uuid,text,uuid,text,boolean),
  club.read_admin_football_location_metrics(text,text)
TO :"admin_auth_role";
REVOKE ALL ON FUNCTION
  club.read_admin_football_access(text,text),
  club.write_admin_football_access(text,text,uuid,text,uuid,text,boolean),
  club.read_admin_football_location_metrics(text,text)
FROM :"app_role";
COMMIT;
