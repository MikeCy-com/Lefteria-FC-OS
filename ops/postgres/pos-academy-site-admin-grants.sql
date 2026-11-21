\set ON_ERROR_STOP on
-- MANUAL STAGING REVIEW ONLY. Never automatically executed by migrations,
-- startup, tests on production, or the mobile runtime.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_role_required'; END $required$;
\endif

BEGIN;
SELECT set_config('lefteria.site_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.site_mobile_role',:'app_role',true);

DO $preflight$
DECLARE admin_role text:=current_setting('lefteria.site_admin_role');
  mobile_role text:=current_setting('lefteria.site_mobile_role');
  f oid;
BEGIN
  IF admin_role=mobile_role OR admin_role=current_user
    OR NOT EXISTS (SELECT 1 FROM pg_roles
      WHERE rolname=admin_role AND rolcanlogin AND NOT rolinherit
        AND NOT rolsuper AND NOT rolbypassrls)
  THEN RAISE EXCEPTION 'pos_site_admin_independence_required'; END IF;
  FOREACH f IN ARRAY ARRAY[
    'pos_cashier.read_admin_pos_sites(text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_pos_site_finances(text,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_pos_site_sales(text,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_site_stock_reconciliation(text,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_terminal_setup(text,text)'::regprocedure::oid,
    'pos_cashier.ensure_admin_academy_pos_site(text,uuid)'::regprocedure::oid,
    'pos_cashier.create_admin_terminal(text,text,text,text)'::regprocedure::oid,
    'pos_cashier.set_admin_terminal_operator(text,text,text,uuid,boolean)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc p
      WHERE p.oid=f AND p.prosecdef
        AND NOT EXISTS(SELECT 1 FROM aclexplode(
          coalesce(p.proacl,acldefault('f',p.proowner))) acl
          WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
    THEN RAISE EXCEPTION 'pos_site_function_is_not_private'; END IF;
  END LOOP;
  IF has_function_privilege(admin_role,
       'pos_cashier.authorize_site_finances(text,text,text)','EXECUTE')
    OR has_function_privilege(mobile_role,
       'pos_cashier.authorize_site_finances(text,text,text)','EXECUTE')
  THEN RAISE EXCEPTION 'pos_site_private_authorizer_exposed'; END IF;
END $preflight$;

GRANT USAGE ON SCHEMA pos_cashier TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_pos_sites(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_pos_site_finances(text,text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_pos_site_sales(text,text,text)
  TO :"admin_auth_role";
-- Review-only, no stock or financial write access.
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_site_stock_reconciliation(
  text,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_terminal_setup(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.ensure_admin_academy_pos_site(text,uuid)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.create_admin_terminal(text,text,text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.set_admin_terminal_operator(
  text,text,text,uuid,boolean) TO :"admin_auth_role";

DO $postflight$
DECLARE a text:=current_setting('lefteria.site_admin_role');
  m text:=current_setting('lefteria.site_mobile_role');
  f oid;t text;
BEGIN
  FOREACH f IN ARRAY ARRAY[
    'pos_cashier.read_admin_pos_sites(text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_pos_site_finances(text,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_pos_site_sales(text,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_site_stock_reconciliation(text,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_terminal_setup(text,text)'::regprocedure::oid,
    'pos_cashier.ensure_admin_academy_pos_site(text,uuid)'::regprocedure::oid,
    'pos_cashier.create_admin_terminal(text,text,text,text)'::regprocedure::oid,
    'pos_cashier.set_admin_terminal_operator(text,text,text,uuid,boolean)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(a,f,'EXECUTE')
      OR has_function_privilege(m,f,'EXECUTE')
    THEN RAISE EXCEPTION 'pos_site_grant_boundary_invalid'; END IF;
  END LOOP;
  FOREACH t IN ARRAY ARRAY[
    'registers','cashier_assignments','shifts','stock','sales','sale_lines',
    'payment_events','receipts','returns','refund_events','finance_posting_outbox'
  ] LOOP
    IF has_table_privilege(a,format('pos_cashier.%I',t),
        'SELECT,INSERT,UPDATE,DELETE')
      OR has_table_privilege(m,format('pos_cashier.%I',t),
        'SELECT,INSERT,UPDATE,DELETE')
    THEN RAISE EXCEPTION 'pos_site_raw_financial_table_access_denied'; END IF;
  END LOOP;
  IF has_table_privilege(a,'pos.academy_football_location_links',
        'SELECT,INSERT,UPDATE,DELETE')
    OR has_table_privilege(m,'pos.academy_football_location_links',
        'SELECT,INSERT,UPDATE,DELETE')
  THEN RAISE EXCEPTION 'pos_site_canonical_link_exposed'; END IF;
END $postflight$;
COMMIT;
