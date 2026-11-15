\set ON_ERROR_STOP on
\set ECHO none
-- Isolated PostgreSQL only, after FinanceSupplierApprovalReversals migration.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'supplier_reversal_admin_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'supplier_reversal_mobile_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.reversal_admin',:'admin_auth_role',true),
       set_config('lefteria.reversal_mobile',:'app_role',true);
DO $grants$
DECLARE
  a text:=current_setting('lefteria.reversal_admin');
  m text:=current_setting('lefteria.reversal_mobile');
  f oid; rel oid;
BEGIN
  IF a=m OR a=current_user OR m=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles
      WHERE rolname=a AND rolcanlogin AND NOT rolsuper
        AND NOT rolbypassrls AND NOT rolinherit)
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=m AND NOT rolsuper)
  THEN RAISE EXCEPTION 'supplier_reversal_runtime_roles_invalid'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'finance.create_admin_supplier_approval_reversal(text,text,uuid,text,uuid,text)'::regprocedure::oid,
    'finance.review_admin_supplier_approval_reversal(text,text,uuid,boolean,text)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=f
      AND p.prosecdef
      AND p.proowner=(SELECT relowner FROM pg_class
        WHERE oid='finance.supplier_bills'::regclass)
      AND EXISTS(SELECT 1 FROM unnest(coalesce(p.proconfig,ARRAY[]::text[]))
        AS conf(entry) WHERE regexp_replace(conf.entry,'[[:space:]]','','g')
          ='search_path=pg_catalog,pg_temp'))
      OR EXISTS(SELECT 1 FROM pg_proc p,
        LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE p.oid=f AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
    THEN RAISE EXCEPTION 'supplier_reversal_function_unprotected'; END IF;
  END LOOP;
  FOREACH rel IN ARRAY ARRAY[
    'finance.supplier_review_reversal_requests'::regclass::oid,
    'finance.supplier_review_reversal_decisions'::regclass::oid
  ] LOOP
    IF has_any_column_privilege(a,rel,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(a,rel,'DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(m,rel,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(m,rel,'DELETE,TRUNCATE,TRIGGER')
    THEN RAISE EXCEPTION 'supplier_reversal_direct_table_access'; END IF;
  END LOOP;
END $grants$;
GRANT USAGE ON SCHEMA finance TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION
  finance.create_admin_supplier_approval_reversal(text,text,uuid,text,uuid,text),
  finance.review_admin_supplier_approval_reversal(text,text,uuid,boolean,text)
  TO :"admin_auth_role";
REVOKE ALL ON FUNCTION
  finance.create_admin_supplier_approval_reversal(text,text,uuid,text,uuid,text),
  finance.review_admin_supplier_approval_reversal(text,text,uuid,boolean,text)
  FROM :"app_role";
COMMIT;
