-- Disposable PostgreSQL only. Validate the actual installed finance
-- functions, not just application menu/endpoint authorization.
\set ON_ERROR_STOP on
BEGIN;
DO $global_finance$
DECLARE
  fn text;
  fn_oid oid;
  fn_def text;
BEGIN
  FOREACH fn IN ARRAY ARRAY[
    'create_admin_payment_request','ready_admin_payment_request',
    'read_admin_payment_requests','issue_admin_training_fee',
    'attach_admin_training_fee_payment','read_admin_training_fees',
    'reserve_admin_training_fee_sms','finish_admin_training_fee_sms',
    'record_admin_training_fee_cash','read_admin_invoice_pdf'
  ] LOOP
    SELECT p.oid INTO fn_oid
      FROM pg_catalog.pg_proc p
      JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
      WHERE n.nspname='finance' AND p.proname=fn;
    IF fn_oid IS NULL THEN
      RAISE EXCEPTION 'Finance function missing: %',fn;
    END IF;
    SELECT pg_catalog.pg_get_functiondef(fn_oid) INTO fn_def;
    IF position('v_role<>''super_admin''' IN fn_def)=0
      OR position('v_role NOT IN (''accountant'',''super_admin'')' IN fn_def)>0
      OR position('v_role NOT IN (''manager'',''super_admin'')' IN fn_def)>0
    THEN RAISE EXCEPTION 'Global finance role boundary missing: %',fn; END IF;
    IF NOT (SELECT p.prosecdef FROM pg_catalog.pg_proc p WHERE p.oid=fn_oid)
    THEN RAISE EXCEPTION 'Finance definer boundary missing: %',fn; END IF;
    IF EXISTS (
      SELECT 1 FROM pg_catalog.pg_proc p,
        LATERAL pg_catalog.aclexplode(
          coalesce(p.proacl,pg_catalog.acldefault('f',p.proowner))) acl
      WHERE p.oid=fn_oid AND acl.grantee=0 AND acl.privilege_type='EXECUTE'
    ) THEN RAISE EXCEPTION 'Global finance callable by PUBLIC: %',fn; END IF;
  END LOOP;
END $global_finance$;
ROLLBACK;
