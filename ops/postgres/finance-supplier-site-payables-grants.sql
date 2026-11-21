\set ON_ERROR_STOP on
\set ECHO none
-- Run only in isolated staging after FinanceSupplierSitePayables migration.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'site_payables_admin_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'site_payables_mobile_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.site_payables_admin',:'admin_auth_role',true),
       set_config('lefteria.site_payables_mobile',:'app_role',true);
DO $grants$
DECLARE a text:=current_setting('lefteria.site_payables_admin');
        m text:=current_setting('lefteria.site_payables_mobile');
        p oid:='finance.read_admin_supplier_site_payables(text,text,date)'::regprocedure::oid;
BEGIN
  IF a=m OR a=current_user OR m=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=a AND rolcanlogin
      AND NOT rolsuper AND NOT rolbypassrls AND NOT rolinherit)
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=m AND NOT rolsuper)
  THEN RAISE EXCEPTION 'site_payables_runtime_roles_invalid'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_proc f WHERE f.oid=p
    AND f.prosecdef AND f.proowner=(
      SELECT relowner FROM pg_class
      WHERE oid='finance.supplier_bills'::regclass)
    AND EXISTS(SELECT 1 FROM unnest(coalesce(f.proconfig,ARRAY[]::text[]))
      AS c(entry) WHERE regexp_replace(c.entry,'[[:space:]]','','g')
        ='search_path=pg_catalog,pg_temp'))
    OR EXISTS(SELECT 1 FROM pg_proc f,
      LATERAL aclexplode(coalesce(f.proacl,acldefault('f',f.proowner))) acl
      WHERE f.oid=p AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
  THEN RAISE EXCEPTION 'site_payables_function_unprotected'; END IF;
  FOREACH p IN ARRAY ARRAY[
    'finance.supplier_bills'::regclass::oid,
    'finance.supplier_payment_requests'::regclass::oid,
    'finance.supplier_payment_decisions'::regclass::oid,
    'finance.supplier_credit_requests'::regclass::oid,
    'finance.supplier_credit_decisions'::regclass::oid
  ] LOOP
    IF has_any_column_privilege(a,p,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(a,p,'DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(m,p,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(m,p,'DELETE,TRUNCATE,TRIGGER')
    THEN RAISE EXCEPTION 'site_payables_direct_table_access'; END IF;
  END LOOP;
END $grants$;
GRANT USAGE ON SCHEMA finance TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION finance.read_admin_supplier_site_payables(text,text,date)
  TO :"admin_auth_role";
REVOKE ALL ON FUNCTION finance.read_admin_supplier_site_payables(text,text,date)
  FROM :"app_role";
COMMIT;
