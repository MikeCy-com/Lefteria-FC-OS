\set ON_ERROR_STOP on
\set ECHO none
-- Apply after FinanceAccountingOverview migration in an isolated OS database,
-- using the migration owner and the existing locked-down web-admin DB login.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'finance_overview_admin_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'finance_overview_mobile_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.overview_admin_role',:'admin_auth_role',true),
       set_config('lefteria.overview_app_role',:'app_role',true);
DO $guard$
DECLARE
  a text:=current_setting('lefteria.overview_admin_role');
  m text:=current_setting('lefteria.overview_app_role');
  f oid:='finance.read_admin_accounting_overview(text,text,date)'::regprocedure::oid;
  rel oid;
BEGIN
  IF a=m OR a=current_user OR m=current_user
    OR NOT EXISTS(SELECT 1 FROM pg_roles
      WHERE rolname=a AND rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolinherit)
    OR NOT EXISTS(SELECT 1 FROM pg_roles
      WHERE rolname=m AND NOT rolsuper)
  THEN RAISE EXCEPTION 'finance_overview_roles_invalid'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE p.oid=f AND p.prosecdef
    AND p.proowner=(SELECT relowner FROM pg_class
      WHERE oid='finance.training_fee_issues'::regclass)
    AND EXISTS(SELECT 1 FROM unnest(coalesce(p.proconfig,ARRAY[]::text[]))
      AS c(entry) WHERE regexp_replace(c.entry,'[[:space:]]','','g')
      ='search_path=pg_catalog,pg_temp'))
    OR EXISTS(SELECT 1 FROM pg_proc p,
      LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
      WHERE p.oid=f AND acl.grantee=0 AND acl.privilege_type='EXECUTE')
  THEN RAISE EXCEPTION 'finance_overview_function_unprotected'; END IF;
  FOREACH rel IN ARRAY ARRAY[
    'finance.training_fee_issues'::regclass::oid,
    'finance.general_charge_issues'::regclass::oid,
    'finance.general_charge_events'::regclass::oid,
    'finance.payment_requests'::regclass::oid
  ] LOOP
    IF has_any_column_privilege(a,rel,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(a,rel,'DELETE,TRUNCATE,TRIGGER')
      OR has_any_column_privilege(m,rel,'SELECT,INSERT,UPDATE,REFERENCES')
      OR has_table_privilege(m,rel,'DELETE,TRUNCATE,TRIGGER')
    THEN RAISE EXCEPTION 'finance_overview_direct_table_access'; END IF;
  END LOOP;
END $guard$;
GRANT USAGE ON SCHEMA finance TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION
  finance.read_admin_accounting_overview(text,text,date) TO :"admin_auth_role";
REVOKE ALL ON FUNCTION
  finance.read_admin_accounting_overview(text,text,date) FROM :"app_role";
COMMIT;
