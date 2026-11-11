\set ON_ERROR_STOP on
-- Disposable database ONLY. Structural security and denied fake-cookie reads.
BEGIN;
DO $pos_checks$
DECLARE
  v_table text;
  v_function text;
  v_public boolean;
  v_denied boolean:=false;
BEGIN
  FOREACH v_table IN ARRAY ARRAY[
    'cities','locations','location_products','location_stock',
    'operators','operator_sessions','sales','sale_lines',
    'sale_payment_events','stock_movements'
  ] LOOP
    IF NOT EXISTS(
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='pos' AND c.relname=v_table AND c.relrowsecurity
    ) THEN RAISE EXCEPTION 'pos_location_missing_rls: %',v_table; END IF;
    SELECT EXISTS(
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      CROSS JOIN LATERAL aclexplode(
        coalesce(c.relacl,acldefault('r',c.relowner))) a
      WHERE n.nspname='pos' AND c.relname=v_table
        AND a.grantee=0 AND a.privilege_type IN (
          'SELECT','INSERT','UPDATE','DELETE')
    ) INTO v_public;
    IF v_public THEN RAISE EXCEPTION 'pos_private_location_public_access: %',v_table; END IF;
  END LOOP;
  FOREACH v_function IN ARRAY ARRAY[
    'pos.read_admin_locations(text,text)',
    'pos.create_admin_city(text,text,text)',
    'pos.create_admin_location(text,text,text,text,text)'
  ] LOOP
    IF NOT EXISTS(
      SELECT 1 FROM pg_proc p
      WHERE p.oid=v_function::regprocedure
        AND p.prosecdef AND NOT EXISTS(
          SELECT 1 FROM aclexplode(coalesce(
            p.proacl,acldefault('f',p.proowner))) a
          WHERE a.grantee=0 AND a.privilege_type='EXECUTE'
        )
    ) THEN RAISE EXCEPTION 'pos_location_function_not_private: %',v_function; END IF;
  END LOOP;
  IF NOT EXISTS(
    SELECT 1 FROM pg_trigger
    WHERE tgname='pos_sale_payment_guard' AND NOT tgisinternal)
    OR NOT EXISTS(
    SELECT 1 FROM pg_trigger
    WHERE tgname='pos_stock_movement_apply' AND NOT tgisinternal)
    OR NOT EXISTS(
    SELECT 1 FROM pg_trigger
    WHERE tgname='pos_verify_sale_header' AND NOT tgisinternal)
  THEN RAISE EXCEPTION 'pos_transaction_guards_missing'; END IF;
  BEGIN
    PERFORM pos.read_admin_locations(
      repeat('0',64),'lefteria_fc');
  EXCEPTION WHEN SQLSTATE '42501' THEN v_denied:=true;
  END;
  IF NOT v_denied THEN RAISE EXCEPTION 'pos_forged_admin_session_accepted'; END IF;
END $pos_checks$;
ROLLBACK;