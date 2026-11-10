\set ON_ERROR_STOP on
-- Operator-only privileges: no raw POS data ever goes to the web app,
-- mobile account role or public website.
\if :{?admin_auth_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'admin_auth_role_required'; END $required$;
\endif
\if :{?app_role}
\else
  DO $required$ BEGIN RAISE EXCEPTION 'mobile_runtime_role_required'; END $required$;
\endif
BEGIN;
SELECT set_config('lefteria.pos_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.pos_mobile_role',:'app_role',true);
DO $preflight$
DECLARE
  v_admin text:=current_setting('lefteria.pos_admin_role');
  v_mobile text:=current_setting('lefteria.pos_mobile_role');
  v_proc oid;
BEGIN
  IF v_admin=v_mobile OR v_admin=current_user OR NOT EXISTS(
    SELECT 1 FROM pg_roles WHERE rolname=v_admin AND rolcanlogin
      AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
  THEN RAISE EXCEPTION 'unsafe_pos_location_admin_role'; END IF;
  FOREACH v_proc IN ARRAY ARRAY[
    'pos.read_admin_locations(text,text)'::regprocedure::oid,
    'pos.create_admin_city(text,text,text)'::regprocedure::oid,
    'pos.create_admin_location(text,text,text,text,text)'::regprocedure::oid,
    'pos.write_admin_location_product(text,text,text,text,boolean,bigint)'::regprocedure::oid,
    'pos.receive_admin_stock(text,text,text,text,text,integer,uuid)'::regprocedure::oid
  ] LOOP
    IF NOT (SELECT prosecdef AND NOT EXISTS(
      SELECT 1 FROM aclexplode(coalesce(proacl,acldefault('f',proowner)))
      WHERE grantee=0 AND privilege_type='EXECUTE')
      FROM pg_proc WHERE oid=v_proc)
    THEN RAISE EXCEPTION 'unsafe_pos_location_function'; END IF;
  END LOOP;
END $preflight$;
GRANT USAGE ON SCHEMA pos TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos.read_admin_locations(text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos.create_admin_city(text,text,text) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos.create_admin_location(text,text,text,text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos.write_admin_location_product(
  text,text,text,text,boolean,bigint) TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos.receive_admin_stock(
  text,text,text,text,text,integer,uuid) TO :"admin_auth_role";

DO $verify$
DECLARE
  v_admin text:=current_setting('lefteria.pos_admin_role');
  v_mobile text:=current_setting('lefteria.pos_mobile_role');
  v_table text;
  v_proc oid;
BEGIN
  FOREACH v_proc IN ARRAY ARRAY[
    'pos.read_admin_locations(text,text)'::regprocedure::oid,
    'pos.create_admin_city(text,text,text)'::regprocedure::oid,
    'pos.create_admin_location(text,text,text,text,text)'::regprocedure::oid,
    'pos.write_admin_location_product(text,text,text,text,boolean,bigint)'::regprocedure::oid,
    'pos.receive_admin_stock(text,text,text,text,text,integer,uuid)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(v_admin,v_proc,'EXECUTE')
      OR has_function_privilege(v_mobile,v_proc,'EXECUTE')
    THEN RAISE EXCEPTION 'pos_location_function_grant_mismatch'; END IF;
  END LOOP;
  FOREACH v_table IN ARRAY ARRAY[
    'cities','locations','location_products','location_stock','operators',
    'operator_sessions','sales','sale_lines','sale_payment_events','stock_movements'
  ] LOOP
    IF has_table_privilege(v_admin,format('pos.%I',v_table),
        'SELECT,INSERT,UPDATE,DELETE')
       OR has_table_privilege(v_mobile,format('pos.%I',v_table),
        'SELECT,INSERT,UPDATE,DELETE')
    THEN RAISE EXCEPTION 'private_pos_location_table_exposed: %',v_table; END IF;
  END LOOP;
  IF has_function_privilege(v_admin,
    'pos.require_admin_location_owner(text,text)','EXECUTE')
  THEN RAISE EXCEPTION 'pos_admin_helper_exposed'; END IF;
END $verify$;
COMMIT;
