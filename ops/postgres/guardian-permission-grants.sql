\set ON_ERROR_STOP on
-- Restricted mobile application role, disposable database first.
-- Never auto-run on a production database or grant raw table privileges.
\if :{?app_role}
\else
  \echo 'app_role must be supplied by an operator'
  \quit 2
\endif
BEGIN;
SELECT set_config('lefteria.guardian_permission_role', :'app_role', true);
DO $guard$
DECLARE role_name text:=current_setting('lefteria.guardian_permission_role');
BEGIN
  IF current_database() IN ('postgres','template0','template1')
    OR role_name=current_user
    OR NOT EXISTS(SELECT 1 FROM pg_roles
      WHERE rolname=role_name AND rolcanlogin
        AND NOT rolsuper AND NOT rolbypassrls)
    OR has_table_privilege(role_name,'identity.family_guardian_permissions',
        'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'guardian_permission_runtime_role_invalid'; END IF;
  IF EXISTS(
    SELECT 1 FROM pg_proc p,
      LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) x
      WHERE p.oid IN(
        'identity.issue_guardian_permission(uuid,text,text,timestamptz,uuid,text,text,uuid)'::regprocedure,
        'identity.read_issued_guardian_permissions(uuid,text,text,timestamptz)'::regprocedure,
        'identity.revoke_issued_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)'::regprocedure,
        'identity.read_recipient_guardian_permissions(uuid,text,text,timestamptz)'::regprocedure,
        'identity.accept_recipient_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)'::regprocedure,
        'identity.decline_recipient_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)'::regprocedure)
        AND x.grantee=0 AND x.privilege_type='EXECUTE'
  ) THEN RAISE EXCEPTION 'guardian_permission_public_execute_detected'; END IF;
END $guard$;
GRANT USAGE ON SCHEMA identity TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.issue_guardian_permission(
    uuid,text,text,timestamptz,uuid,text,text,uuid) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.read_issued_guardian_permissions(
    uuid,text,text,timestamptz) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.revoke_issued_guardian_permission(
    uuid,text,text,timestamptz,uuid,uuid) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.read_recipient_guardian_permissions(
    uuid,text,text,timestamptz) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.accept_recipient_guardian_permission(
    uuid,text,text,timestamptz,uuid,uuid) TO :"app_role";
GRANT EXECUTE ON FUNCTION identity.decline_recipient_guardian_permission(
    uuid,text,text,timestamptz,uuid,uuid) TO :"app_role";
DO $verify$
DECLARE role_name text:=current_setting('lefteria.guardian_permission_role');
BEGIN
  IF NOT has_function_privilege(role_name,
      'identity.issue_guardian_permission(uuid,text,text,timestamptz,uuid,text,text,uuid)','EXECUTE')
    OR NOT has_function_privilege(role_name,
      'identity.read_issued_guardian_permissions(uuid,text,text,timestamptz)','EXECUTE')
    OR NOT has_function_privilege(role_name,
      'identity.revoke_issued_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)','EXECUTE')
    OR NOT has_function_privilege(role_name,
      'identity.read_recipient_guardian_permissions(uuid,text,text,timestamptz)','EXECUTE')
    OR NOT has_function_privilege(role_name,
      'identity.accept_recipient_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)','EXECUTE')
    OR NOT has_function_privilege(role_name,
      'identity.decline_recipient_guardian_permission(uuid,text,text,timestamptz,uuid,uuid)','EXECUTE')
    OR has_table_privilege(role_name,'identity.family_guardian_permissions',
      'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'guardian_permission_scope_invalid'; END IF;
END $verify$;
COMMIT;
