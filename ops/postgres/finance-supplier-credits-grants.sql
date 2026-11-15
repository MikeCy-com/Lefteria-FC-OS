\set ON_ERROR_STOP on
\set ECHO none
-- Staging only, after FinanceSupplierCreditNotes migration.
\if :{?admin_auth_role}
\else
 DO $required$ BEGIN RAISE EXCEPTION 'supplier_credit_admin_role_required'; END $required$;
\endif
\if :{?app_role}
\else
 DO $required$ BEGIN RAISE EXCEPTION 'supplier_credit_mobile_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.credit_admin',:'admin_auth_role',true),
       set_config('lefteria.credit_mobile',:'app_role',true);
DO $grants$
DECLARE a text:=current_setting('lefteria.credit_admin');
        m text:=current_setting('lefteria.credit_mobile');
        p oid; rel oid;
BEGIN
 IF a=m OR a=current_user OR m=current_user OR
   NOT EXISTS(SELECT 1 FROM pg_roles
     WHERE rolname=a AND rolcanlogin AND NOT rolsuper
       AND NOT rolbypassrls AND NOT rolinherit)
   OR NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname=m AND NOT rolsuper)
 THEN RAISE EXCEPTION 'supplier_credit_roles_invalid'; END IF;
 FOREACH p IN ARRAY ARRAY[
  'finance.create_admin_supplier_credit_request(text,text,uuid,uuid,text,date,bigint,text)'::regprocedure::oid,
  'finance.review_admin_supplier_credit_request(text,text,uuid,boolean,text)'::regprocedure::oid,
  'finance.read_admin_supplier_payable_position(text,text,date)'::regprocedure::oid,
  'finance.check_supplier_payment_credit_ceiling()'::regprocedure::oid
 ] LOOP
  IF NOT EXISTS(SELECT 1 FROM pg_proc f WHERE f.oid=p
    AND f.prosecdef
    AND f.proowner=(SELECT relowner FROM pg_class
       WHERE oid='finance.supplier_bills'::regclass)
    AND EXISTS(SELECT 1 FROM unnest(coalesce(f.proconfig,ARRAY[]::text[]))
      AS c(entry) WHERE regexp_replace(c.entry,'[[:space:]]','','g')
        ='search_path=pg_catalog,pg_temp'))
    OR EXISTS(SELECT 1 FROM pg_proc f,
      LATERAL aclexplode(coalesce(f.proacl,acldefault('f',f.proowner))) acl
      WHERE f.oid=p AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
  THEN RAISE EXCEPTION 'supplier_credit_function_unprotected'; END IF;
 END LOOP;
 FOREACH rel IN ARRAY ARRAY[
  'finance.supplier_credit_requests'::regclass::oid,
  'finance.supplier_credit_decisions'::regclass::oid
 ] LOOP
  IF has_any_column_privilege(a,rel,'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(a,rel,'DELETE,TRUNCATE,TRIGGER')
    OR has_any_column_privilege(m,rel,'SELECT,INSERT,UPDATE,REFERENCES')
    OR has_table_privilege(m,rel,'DELETE,TRUNCATE,TRIGGER')
  THEN RAISE EXCEPTION 'supplier_credit_direct_table_access'; END IF;
 END LOOP;
END $grants$;
GRANT USAGE ON SCHEMA finance TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION
 finance.create_admin_supplier_credit_request(text,text,uuid,uuid,text,date,bigint,text),
 finance.review_admin_supplier_credit_request(text,text,uuid,boolean,text),
 finance.read_admin_supplier_payable_position(text,text,date)
 TO :"admin_auth_role";
REVOKE ALL ON FUNCTION
 finance.create_admin_supplier_credit_request(text,text,uuid,uuid,text,date,bigint,text),
 finance.review_admin_supplier_credit_request(text,text,uuid,boolean,text),
 finance.read_admin_supplier_payable_position(text,text,date),
 finance.check_supplier_payment_credit_ceiling()
 FROM :"app_role";
REVOKE ALL ON FUNCTION finance.check_supplier_payment_credit_ceiling()
 FROM :"admin_auth_role";
COMMIT;
