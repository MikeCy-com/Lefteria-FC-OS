\set ON_ERROR_STOP on
-- OPERATOR-APPROVED ACTION, NEVER run automatically during migration.
-- The independent web-admin login role must have no direct protected
-- club roster/Academy group metadata table privileges.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'academy_admin_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.academy_profile_role',:'admin_auth_role',true);
DO $guard$
DECLARE app text:=current_setting('lefteria.academy_profile_role');
BEGIN
  IF app=current_user OR NOT EXISTS(
     SELECT 1 FROM pg_roles WHERE rolname=app
       AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls)
    OR has_table_privilege(app,'club.academy_group_profiles',
      'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'academy_profile_role_boundary_failed'; END IF;
  IF EXISTS(
    SELECT 1 FROM pg_proc p,
      LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
    WHERE p.oid IN (
      'club.authorize_admin_academy_profile(text)'::regprocedure,
      'club.read_admin_academy_group_profile(text,text)'::regprocedure,
      'club.write_admin_academy_group_profile(text,text,bigint,jsonb)'::regprocedure)
      AND acl.grantee=0 AND acl.privilege_type='EXECUTE'
  ) THEN RAISE EXCEPTION 'academy_profile_public_function_detected'; END IF;
END $guard$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.read_admin_academy_group_profile(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.write_admin_academy_group_profile(text,text,bigint,jsonb)
  TO :"admin_auth_role";
DO $verify$
DECLARE app text:=current_setting('lefteria.academy_profile_role');
BEGIN
  IF NOT has_function_privilege(app,
      'club.read_admin_academy_group_profile(text,text)','EXECUTE')
     OR NOT has_function_privilege(app,
      'club.write_admin_academy_group_profile(text,text,bigint,jsonb)','EXECUTE')
     OR has_function_privilege(app,
      'club.authorize_admin_academy_profile(text)','EXECUTE')
     OR has_table_privilege(app,'club.academy_group_profiles',
      'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'academy_profile_grant_scope_failed'; END IF;
END $verify$;
COMMIT;
