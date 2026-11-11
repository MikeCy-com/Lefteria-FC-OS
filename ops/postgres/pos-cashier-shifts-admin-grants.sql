\set ON_ERROR_STOP on
-- EXPLICIT, reviewed operator action only. No automatic grant in migrations.
-- Requires dedicated independent web-admin DB role and distinct mobile role.
\if :{?admin_auth_role}
\else
  DO $needed$ BEGIN RAISE EXCEPTION 'pos_admin_auth_role_required'; END $needed$;
\endif
\if :{?app_role}
\else
  DO $needed$ BEGIN RAISE EXCEPTION 'pos_mobile_role_required'; END $needed$;
\endif

BEGIN;
SELECT set_config('lefteria.pos_cashier_admin_role',:'admin_auth_role',true);
SELECT set_config('lefteria.pos_cashier_mobile_role',:'app_role',true);

DO $check$
DECLARE
  a text:=current_setting('lefteria.pos_cashier_admin_role');
  m text:=current_setting('lefteria.pos_cashier_mobile_role');
  f oid;
BEGIN
  IF a=m OR a=current_user
    OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=a AND rolcanlogin
      AND NOT rolinherit AND NOT rolsuper AND NOT rolbypassrls)
    OR NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname=m)
  THEN RAISE EXCEPTION 'pos_cashier_role_boundary_invalid'; END IF;

  FOREACH f IN ARRAY ARRAY[
    'pos_cashier.read_admin_cashier_registers(text,text)'::regprocedure::oid,
    'pos_cashier.open_admin_cashier_shift(text,text,text,bigint,uuid)'::regprocedure::oid,
    'pos_cashier.close_admin_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS (SELECT 1 FROM pg_proc p
      JOIN pg_language l ON l.oid=p.prolang
      WHERE p.oid=f AND p.prosecdef AND l.lanname='plpgsql'
        AND NOT EXISTS (SELECT 1 FROM aclexplode(
          coalesce(p.proacl,acldefault('f',p.proowner))) x
          WHERE x.grantee=0 AND x.privilege_type='EXECUTE'))
    THEN RAISE EXCEPTION 'pos_cashier_definer_unhardened'; END IF;
  END LOOP;
  IF has_function_privilege(a,
    'pos_cashier.current_cashier_authority(text,text,text)','EXECUTE')
    OR has_function_privilege(m,
    'pos_cashier.current_cashier_authority(text,text,text)','EXECUTE')
  THEN RAISE EXCEPTION 'pos_cashier_helper_exposed'; END IF;
END $check$;

GRANT USAGE ON SCHEMA pos_cashier TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_cashier_registers(text,text)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.open_admin_cashier_shift(text,text,text,bigint,uuid)
  TO :"admin_auth_role";
GRANT EXECUTE ON FUNCTION pos_cashier.close_admin_cashier_shift(text,text,uuid,bigint,uuid)
  TO :"admin_auth_role";

DO $verify$
DECLARE
  a text:=current_setting('lefteria.pos_cashier_admin_role');
  m text:=current_setting('lefteria.pos_cashier_mobile_role');
  f oid; t text;
BEGIN
  FOREACH f IN ARRAY ARRAY[
    'pos_cashier.read_admin_cashier_registers(text,text)'::regprocedure::oid,
    'pos_cashier.open_admin_cashier_shift(text,text,text,bigint,uuid)'::regprocedure::oid,
    'pos_cashier.close_admin_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure::oid
  ] LOOP
    IF NOT has_function_privilege(a,f,'EXECUTE')
      OR has_function_privilege(m,f,'EXECUTE')
    THEN RAISE EXCEPTION 'pos_cashier_execute_scope_invalid'; END IF;
  END LOOP;
  FOREACH t IN ARRAY ARRAY[
    'locations','registers','cashier_assignments','shifts','stock','sales',
    'sale_lines','payment_events','receipts','returns','return_lines',
    'refund_events','transaction_audit'
  ] LOOP
    IF has_table_privilege(a,format('pos_cashier.%I',t),'SELECT,INSERT,UPDATE,DELETE')
       OR has_table_privilege(m,format('pos_cashier.%I',t),'SELECT,INSERT,UPDATE,DELETE')
    THEN RAISE EXCEPTION 'pos_cashier_raw_table_exposed'; END IF;
  END LOOP;
END $verify$;
COMMIT;