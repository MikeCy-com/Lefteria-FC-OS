\set ON_ERROR_STOP on
-- Operator-reviewed, separate web-admin role. Do not run on live VPS
-- without exact-SHA PostgreSQL, role and E2E test evidence.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'profiled_group_admin_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.profiled_group_auth_role',:'admin_auth_role',true);
DO $check$
DECLARE a text:=current_setting('lefteria.profiled_group_auth_role');
BEGIN
  IF a=current_user OR NOT EXISTS (
    SELECT 1 FROM pg_roles WHERE rolname=a AND rolcanlogin
      AND NOT rolsuper AND NOT rolbypassrls
  ) OR has_table_privilege(a,'club.admin_group_create_requests','SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'profiled_group_role_boundary_invalid'; END IF;
  IF EXISTS(SELECT 1 FROM pg_proc p,
    LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
    WHERE p.oid='club.create_admin_group_with_profile(text,text,text,text,uuid,jsonb)'::regprocedure
      AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
  THEN RAISE EXCEPTION 'profiled_group_public_execute_detected'; END IF;
END $check$;
GRANT USAGE ON SCHEMA club TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION club.create_admin_group_with_profile(
    text,text,text,text,uuid,jsonb) TO :"admin_auth_role";
DO $verify$
DECLARE a text:=current_setting('lefteria.profiled_group_auth_role');
BEGIN
  IF NOT has_function_privilege(a,
      'club.create_admin_group_with_profile(text,text,text,text,uuid,jsonb)','EXECUTE')
    OR has_table_privilege(a,'club.admin_group_create_requests','SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'profiled_group_grant_scope_invalid'; END IF;
END $verify$;
COMMIT;
