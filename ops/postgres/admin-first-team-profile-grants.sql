\set ON_ERROR_STOP on
-- REVIEWED OPERATOR-ONLY ACTION for the independent admin auth login role.
-- Never execute against production without migrations, security and API tests.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'team_profile_admin_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.team_profile_role',:'admin_auth_role',true);
DO $guard$
DECLARE role_name text:=current_setting('lefteria.team_profile_role');
BEGIN
  IF role_name=current_user OR NOT EXISTS(
      SELECT 1 FROM pg_roles WHERE rolname=role_name AND rolcanlogin
        AND NOT rolsuper AND NOT rolbypassrls)
    OR has_table_privilege(role_name,'club.first_team_profiles',
      'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'team_profile_role_boundary_invalid'; END IF;
  IF EXISTS(
    SELECT 1 FROM pg_proc p,
      LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
    WHERE p.oid IN (
      'club.authorize_admin_first_team_profile(text)'::regprocedure,
      'club.read_admin_first_team_profile(text,text)'::regprocedure,
      'club.write_admin_first_team_profile(text,text,bigint,jsonb)'::regprocedure
    ) AND acl.grantee=0 AND acl.privilege_type='EXECUTE'
  ) THEN RAISE EXCEPTION 'team_profile_public_execution_detected'; END IF;
END $guard$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_first_team_profile(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.write_admin_first_team_profile(text,text,bigint,jsonb)
  TO :"admin_auth_role";
DO $verify$
DECLARE role_name text:=current_setting('lefteria.team_profile_role');
BEGIN
  IF NOT has_function_privilege(role_name,
      'club.read_admin_first_team_profile(text,text)','EXECUTE')
    OR NOT has_function_privilege(role_name,
      'club.write_admin_first_team_profile(text,text,bigint,jsonb)','EXECUTE')
    OR has_function_privilege(role_name,
      'club.authorize_admin_first_team_profile(text)','EXECUTE')
    OR has_table_privilege(role_name,'club.first_team_profiles',
      'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'team_profile_grant_scope_invalid'; END IF;
END $verify$;
COMMIT;
