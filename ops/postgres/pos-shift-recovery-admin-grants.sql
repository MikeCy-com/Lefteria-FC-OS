\set ON_ERROR_STOP on
-- MANUAL, STAGING-ONLY supervisor recovery grant after independent approval.
-- This SQL is never executed by application migrations, bootstrap or CI.
-- Usage: psql -v admin_auth_role=... -v app_role=... -v recovery_review=staging_approved -f ...
\if :{?admin_auth_role}
\else
  DO $needed$ BEGIN RAISE EXCEPTION 'pos_recovery_admin_auth_role_required'; END $needed$;
\endif
\if :{?app_role}
\else
  DO $needed$ BEGIN RAISE EXCEPTION 'pos_recovery_mobile_role_required'; END $needed$;
\endif
\if :{?recovery_review}
\else
  DO $needed$ BEGIN RAISE EXCEPTION 'pos_recovery_review_required'; END $needed$;
\endif

BEGIN;
SELECT set_config('lefteria.pos_recovery_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.pos_recovery_mobile_role',:'app_role',true);
SELECT set_config('lefteria.pos_recovery_approval',:'recovery_review',true);

DO $guard$
DECLARE
  a text:=current_setting('lefteria.pos_recovery_admin_role');
  m text:=current_setting('lefteria.pos_recovery_mobile_role');
  approval text:=current_setting('lefteria.pos_recovery_approval');
  f oid:='pos_cashier.recover_suspended_admin_cashier_shift(text,text,uuid,bigint,uuid,text)'::regprocedure::oid;
  q oid:='pos_cashier.read_admin_shift_recovery_review_queue(text,text)'::regprocedure::oid;
  r oid:='pos_cashier.review_recovered_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure::oid;
BEGIN
  IF approval<>'staging_approved' OR a=m OR a=current_user
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=a AND rolcanlogin
      AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=m)
  THEN RAISE EXCEPTION 'pos_recovery_explicit_staging_approval_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=f AND p.prosecdef
    AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
      WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
  THEN RAISE EXCEPTION 'pos_recovery_function_not_hardened'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=q AND p.prosecdef
    AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
      WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
    OR NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=r AND p.prosecdef
      AND NOT EXISTS(SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
  THEN RAISE EXCEPTION 'pos_second_count_functions_not_hardened'; END IF;
  IF has_function_privilege(m,f,'EXECUTE') OR
    has_function_privilege(m,q,'EXECUTE') OR has_function_privilege(m,r,'EXECUTE')
  THEN RAISE EXCEPTION 'pos_recovery_mobile_execute_exposed'; END IF;
END $guard$;

GRANT USAGE ON SCHEMA pos_cashier TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.recover_suspended_admin_cashier_shift(
  text,text,uuid,bigint,uuid,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_shift_recovery_review_queue(
  text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.review_recovered_cashier_shift(
  text,text,uuid,bigint,uuid) TO :"admin_auth_role";

DO $verify$
DECLARE
  a text:=current_setting('lefteria.pos_recovery_admin_role');
  m text:=current_setting('lefteria.pos_recovery_mobile_role');
  f oid:='pos_cashier.recover_suspended_admin_cashier_shift(text,text,uuid,bigint,uuid,text)'::regprocedure::oid;
  q oid:='pos_cashier.read_admin_shift_recovery_review_queue(text,text)'::regprocedure::oid;
  r oid:='pos_cashier.review_recovered_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure::oid;
BEGIN
  IF NOT has_function_privilege(a,f,'EXECUTE')
    OR NOT has_function_privilege(a,q,'EXECUTE')
    OR NOT has_function_privilege(a,r,'EXECUTE')
    OR has_function_privilege(m,f,'EXECUTE')
    OR has_function_privilege(m,q,'EXECUTE')
    OR has_function_privilege(m,r,'EXECUTE')
    OR has_table_privilege(a,'pos_cashier.shifts','SELECT,INSERT,UPDATE,DELETE')
    OR has_table_privilege(m,'pos_cashier.shifts','SELECT,INSERT,UPDATE,DELETE')
    OR has_table_privilege(a,'pos_cashier.shift_recovery_reviews','SELECT,INSERT,UPDATE,DELETE')
    OR has_table_privilege(m,'pos_cashier.shift_recovery_reviews','SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'pos_recovery_grant_scope_invalid'; END IF;
END $verify$;
COMMIT;
