\set ON_ERROR_STOP on
-- Rollback-only structural verification against an isolated migrated database.
-- Never run on production; no cashier transaction or payment is generated.
BEGIN;
DO $checks$
DECLARE name text;
BEGIN
  FOREACH name IN ARRAY ARRAY[
    'locations','registers','cashier_assignments','shifts','stock','sales',
    'sale_lines','payment_events','receipt_sequences','receipts','returns',
    'return_lines','refund_events','transaction_audit'
  ] LOOP
    IF NOT EXISTS(
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='pos' AND c.relname=name AND c.relrowsecurity
    ) THEN RAISE EXCEPTION 'pos_operational_rls_missing: %',name; END IF;
    IF EXISTS(
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      CROSS JOIN LATERAL aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) a
      WHERE n.nspname='pos' AND c.relname=name
        AND a.grantee=0 AND a.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE')
    ) THEN RAISE EXCEPTION 'pos_operational_public_privilege: %',name; END IF;
  END LOOP;
  IF NOT EXISTS(SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos.receipts'::regclass AND tgname='pos_receipt_paid_guard'
      AND NOT tgisinternal)
  OR NOT EXISTS(SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos.return_lines'::regclass
      AND tgname='pos_return_quantity_guard' AND NOT tgisinternal)
  OR NOT EXISTS(SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos.refund_events'::regclass
      AND tgname='pos_refund_original_payment_guard' AND NOT tgisinternal)
  THEN RAISE EXCEPTION 'pos_operational_financial_guard_missing'; END IF;
END $checks$;
ROLLBACK;
