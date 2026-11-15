\set ON_ERROR_STOP on
\set ECHO none
-- Run only after FinanceSupplierBillRegister migration on the isolated OS DB.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'supplier_bill_admin_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'supplier_bill_mobile_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.supplier_admin_role',:'admin_auth_role',true),
       set_config('lefteria.supplier_app_role',:'app_role',true);
DO $grants$
DECLARE
  a text:=current_setting('lefteria.supplier_admin_role');
  m text:=current_setting('lefteria.supplier_app_role');
  p oid;
BEGIN
  IF a=m OR a=current_user OR m=current_user OR
    NOT EXISTS(SELECT 1 FROM pg_roles
      WHERE rolname=a AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolinherit)
    OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=m AND NOT rolsuper)
  THEN RAISE EXCEPTION 'supplier_bill_roles_invalid'; END IF;
  FOREACH p IN ARRAY ARRAY[
    'finance.authorize_admin_supplier_bills(text,text)'::regprocedure::oid,
    'finance.create_admin_supplier_bill(text,text,uuid,text,text,text,text,date,bigint,uuid)'::regprocedure::oid,
    'finance.read_admin_supplier_bills(text,text,date)'::regprocedure::oid,
    'finance.read_admin_supplier_site_totals(text,text,date)'::regprocedure::oid
  ] LOOP
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
    THEN RAISE EXCEPTION 'supplier_bill_function_unprotected'; END IF;
  END LOOP;
  IF has_any_column_privilege(a,'finance.supplier_bills','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(a,'finance.supplier_bills','DELETE,TRUNCATE,TRIGGER')
    OR has_any_column_privilege(m,'finance.supplier_bills','SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(m,'finance.supplier_bills','DELETE,TRUNCATE,TRIGGER')
  THEN RAISE EXCEPTION 'supplier_bill_direct_table_access'; END IF;
END $grants$;
GRANT USAGE ON SCHEMA finance TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION
  finance.create_admin_supplier_bill(
    text,text,uuid,text,text,text,text,date,bigint,uuid),
  finance.read_admin_supplier_bills(text,text,date),
  finance.read_admin_supplier_site_totals(text,text,date)
  TO :"admin_auth_role";
REVOKE ALL ON FUNCTION
  finance.authorize_admin_supplier_bills(text,text),
  finance.create_admin_supplier_bill(text,text,uuid,text,text,text,text,date,bigint,uuid),
  finance.read_admin_supplier_bills(text,text,date),
  finance.read_admin_supplier_site_totals(text,text,date)
  FROM :"app_role";
REVOKE ALL ON FUNCTION finance.authorize_admin_supplier_bills(text,text)
  FROM :"admin_auth_role";
COMMIT;
