\set ON_ERROR_STOP on
-- Rollback-only structural verification against an isolated migrated database.
-- Never run on production; no cashier transaction or payment is generated.
BEGIN;
DO $checks$
DECLARE name text;
BEGIN
  FOREACH name IN ARRAY ARRAY[
    'registers','cashier_assignments','shifts','stock','sales',
    'sale_lines','payment_events','receipt_sequences','receipts','returns',
    'return_lines','refund_events','transaction_audit','checkout_policies','sale_reservations',
    'vat_treatments','variant_vat_assignments','payabl_notification_inbox',
    'finance_posting_outbox'
  ] LOOP
    IF NOT EXISTS(
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='pos_cashier' AND c.relname=name AND c.relrowsecurity
    ) THEN RAISE EXCEPTION 'pos_operational_rls_missing: %',name; END IF;
    IF EXISTS(
      SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      CROSS JOIN LATERAL aclexplode(coalesce(c.relacl,acldefault('r',c.relowner))) a
      WHERE n.nspname='pos_cashier' AND c.relname=name
        AND a.grantee=0 AND a.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE')
    ) THEN RAISE EXCEPTION 'pos_operational_public_privilege: %',name; END IF;
  END LOOP;
  IF NOT EXISTS(SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos_cashier.receipts'::regclass AND tgname='pos_receipt_paid_guard'
      AND NOT tgisinternal)
  OR NOT EXISTS(SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos_cashier.return_lines'::regclass
      AND tgname='pos_return_quantity_guard' AND NOT tgisinternal)
  OR NOT EXISTS(SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos_cashier.refund_events'::regclass
      AND tgname='pos_refund_original_payment_guard' AND NOT tgisinternal)
  THEN RAISE EXCEPTION 'pos_operational_financial_guard_missing'; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos_cashier.receipts'::regclass
      AND tgname='pos_receipt_fulfill_inventory' AND NOT tgisinternal)
  THEN RAISE EXCEPTION 'pos_receipt_inventory_fulfillment_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_indexes
    WHERE schemaname='pos_cashier' AND indexname='uq_pos_void_request')
  THEN RAISE EXCEPTION 'pos_sale_cancel_replay_index_missing'; END IF;
  IF EXISTS(
    SELECT 1 FROM pg_proc p
    WHERE p.oid IN (
      'pos_cashier.prepare_cashier_checkout(text,text,text,uuid,uuid,jsonb)'::regprocedure,
      'pos_cashier.cancel_unpaid_cashier_sale(text,text,uuid,uuid)'::regprocedure
    ) AND EXISTS(
      SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a
      WHERE a.grantee=0 AND a.privilege_type='EXECUTE'
    )
  ) THEN RAISE EXCEPTION 'pos_checkout_exposed_to_public'; END IF;
  -- New cash tender and reconciliation functions must remain private
  -- and fiscal policy must be disabled in an unprovisioned disposable DB.
  IF EXISTS(SELECT 1 FROM pos_cashier.checkout_policies WHERE enabled)
    OR EXISTS(SELECT 1 FROM pos_cashier.receipt_policies WHERE enabled)
  THEN RAISE EXCEPTION 'pos_cash_tender_fiscal_policy_auto_enabled'; END IF;
  IF NOT EXISTS(
    SELECT 1 FROM pg_constraint
      WHERE conrelid='pos_cashier.payment_events'::regclass
        AND conname='ck_pos_cash_tender_totals'
  ) THEN RAISE EXCEPTION 'pos_cash_tender_constraint_missing'; END IF;
  IF EXISTS (
    SELECT 1 FROM pg_proc p
    WHERE p.oid IN (
      'pos_cashier.settle_exact_cash_sale(text,text,uuid,uuid,bigint)'::regprocedure,
      'pos_cashier.read_cashier_shift_reconciliation(text,text,uuid)'::regprocedure
    ) AND (
      NOT p.prosecdef OR EXISTS (
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
          WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE')
    )
  ) THEN RAISE EXCEPTION 'pos_cash_tender_or_report_unrestricted'; END IF;
  IF EXISTS(SELECT 1 FROM pos_cashier.vat_treatments WHERE enabled)
  THEN RAISE EXCEPTION 'pos_vat_treatments_unreviewed'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_indexes
    WHERE schemaname='pos_cashier' AND indexname='uq_pos_return_review_request')
  THEN RAISE EXCEPTION 'pos_return_review_replay_index_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos_cashier.payabl_notification_inbox'::regclass
      AND tgname='pos_payabl_inbox_immutable' AND NOT tgisinternal)
  THEN RAISE EXCEPTION 'pos_payabl_inbox_mutation_guard_missing'; END IF;
  IF EXISTS (
    SELECT 1 FROM pg_proc p
    WHERE p.oid IN (
      'pos_cashier.request_assigned_cashier_return(text,text,uuid,uuid,text,jsonb)'::regprocedure,
      'pos_cashier.review_cashier_return(text,text,uuid,uuid,boolean)'::regprocedure,
      'pos_cashier.read_assigned_cashier_receipt(text,text,uuid)'::regprocedure
    ) AND (
      NOT p.prosecdef OR EXISTS (
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a
          WHERE a.grantee=0 AND a.privilege_type='EXECUTE')
    )
  ) THEN RAISE EXCEPTION 'pos_return_or_receipt_function_exposed'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_indexes
    WHERE schemaname='pos_cashier' AND indexname='uq_pos_refund_request')
  THEN RAISE EXCEPTION 'pos_refund_request_replay_index_missing'; END IF;
  IF EXISTS (
    SELECT 1 FROM pg_proc p
    WHERE p.oid='pos_cashier.request_approved_return_refund(text,text,uuid,uuid,uuid,bigint)'::regprocedure
      AND (NOT p.prosecdef OR EXISTS (
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'
      ))
  ) THEN RAISE EXCEPTION 'pos_refund_request_function_exposed'; END IF;
  IF NOT EXISTS(
    SELECT 1 FROM pg_trigger
      WHERE tgrelid='pos_cashier.receipts'::regclass
        AND tgname='pos_finance_sale_receipted' AND NOT tgisinternal
  ) OR NOT EXISTS(
    SELECT 1 FROM pg_trigger
      WHERE tgrelid='pos_cashier.refund_events'::regclass
        AND tgname='pos_finance_refund_verified' AND NOT tgisinternal
  ) OR NOT EXISTS(
    SELECT 1 FROM pg_trigger
      WHERE tgrelid='pos_cashier.finance_posting_outbox'::regclass
        AND tgname='pos_finance_outbox_immutable' AND NOT tgisinternal
  ) THEN RAISE EXCEPTION 'pos_finance_outbox_guards_missing'; END IF;
  IF NOT EXISTS(
    SELECT 1 FROM pg_constraint
      WHERE conrelid='pos_cashier.finance_posting_outbox'::regclass
        AND contype='u'
  ) THEN RAISE EXCEPTION 'pos_finance_outbox_idempotency_missing'; END IF;
  IF NOT EXISTS(
    SELECT 1 FROM pg_proc
      WHERE oid='pos_cashier.read_admin_finance_handoff(text,text)'::regprocedure
        AND prosecdef
  ) OR EXISTS(
    SELECT 1 FROM pg_proc p,
      LATERAL aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) a
      WHERE p.oid='pos_cashier.read_admin_finance_handoff(text,text)'::regprocedure
        AND a.grantee=0 AND a.privilege_type='EXECUTE'
  ) THEN RAISE EXCEPTION 'pos_finance_handoff_private_reader_missing'; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_proc p
      WHERE p.oid='pos_cashier.read_admin_cashier_registers(text,text)'::regprocedure
        AND p.prosecdef)
    OR NOT EXISTS (SELECT 1 FROM pg_proc p
      WHERE p.oid='pos_cashier.open_admin_cashier_shift(text,text,text,bigint,uuid)'::regprocedure
        AND p.prosecdef)
    OR NOT EXISTS (SELECT 1 FROM pg_proc p
      WHERE p.oid='pos_cashier.close_admin_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure
        AND p.prosecdef)
  THEN RAISE EXCEPTION 'pos_cashier_shift_definers_missing'; END IF;
  IF EXISTS (
    SELECT 1 FROM pg_proc p
    WHERE p.oid IN (
      'pos_cashier.read_admin_cashier_registers(text,text)'::regprocedure,
      'pos_cashier.open_admin_cashier_shift(text,text,text,bigint,uuid)'::regprocedure,
      'pos_cashier.close_admin_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure,
      'pos_cashier.current_cashier_authority(text,text,text)'::regprocedure
    )
    AND EXISTS (SELECT 1 FROM aclexplode(
      coalesce(p.proacl,acldefault('f',p.proowner))) acl
      WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE')
  ) THEN RAISE EXCEPTION 'pos_cashier_public_execution_granted'; END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_indexes
    WHERE schemaname='pos_cashier' AND indexname='uq_pos_shift_open_request')
    OR NOT EXISTS (SELECT 1 FROM pg_indexes
    WHERE schemaname='pos_cashier' AND indexname='uq_pos_shift_close_request')
  THEN RAISE EXCEPTION 'pos_cashier_idempotency_index_missing'; END IF;
  -- Canonical Academy POS locations and manager-specific financial report
  -- are isolated from the mobile role and raw payment ledger.
  IF NOT EXISTS(SELECT 1 FROM pg_class c
    WHERE c.oid='pos.academy_football_location_links'::regclass
      AND c.relrowsecurity)
  THEN RAISE EXCEPTION 'pos_canonical_location_rls_missing'; END IF;
  IF EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid IN (
      'pos_cashier.authorize_site_finances(text,text,text)'::regprocedure,
      'pos_cashier.ensure_admin_academy_pos_site(text,uuid)'::regprocedure,
      'pos_cashier.read_admin_pos_sites(text,text)'::regprocedure,
      'pos_cashier.read_admin_pos_site_finances(text,text,text)'::regprocedure,
      'pos_cashier.read_admin_pos_site_sales(text,text,text)'::regprocedure,
      'pos_cashier.read_admin_terminal_setup(text,text)'::regprocedure,
      'pos_cashier.create_admin_terminal(text,text,text,text)'::regprocedure,
      'pos_cashier.set_admin_terminal_operator(text,text,text,uuid,boolean)'::regprocedure
    ) AND (NOT p.prosecdef OR EXISTS (
       SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
       WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE')))
  THEN RAISE EXCEPTION 'pos_site_boundary_exposed'; END IF;
  -- Supervised recovery remains SECURITY DEFINER, private and requires
  -- evidence on the closed shift; an unapproved environment has no runtime grants.
  IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE
    conrelid='pos_cashier.shifts'::regclass AND
    conname='ck_pos_shift_recovery_evidence')
    OR NOT EXISTS(SELECT 1 FROM pg_constraint WHERE
    conrelid='pos_cashier.transaction_audit'::regclass
    AND conname='transaction_audit_action_check'
    AND pg_get_constraintdef(oid) LIKE '%shift_recovery_close%')
  THEN RAISE EXCEPTION 'pos_shift_recovery_evidence_guard_missing'; END IF;
  IF EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid='pos_cashier.recover_suspended_admin_cashier_shift(text,text,uuid,bigint,uuid,text)'::regprocedure
      AND (NOT p.prosecdef OR EXISTS(
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE')))
  THEN RAISE EXCEPTION 'pos_shift_recovery_public_exposure'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_proc p WHERE
    p.oid='pos_cashier.recover_suspended_admin_cashier_shift(text,text,uuid,bigint,uuid,text)'::regprocedure
      AND p.prosecdef)
  THEN RAISE EXCEPTION 'pos_shift_recovery_function_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid='pos_cashier.read_assigned_cashier_sellable_stock(text,text,text,uuid)'::regprocedure
      AND p.prosecdef
      AND pg_get_functiondef(p.oid) LIKE '%cashier_catalog_shift_not_owned%'
      AND NOT EXISTS(
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
  THEN RAISE EXCEPTION 'pos_cashier_site_inventory_private_reader_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid='pos_cashier.read_admin_site_stock_reconciliation(text,text,text)'::regprocedure
      AND p.prosecdef
      AND pg_get_functiondef(p.oid) LIKE '%pos_cashier.authorize_site_finances%'
      AND pg_get_functiondef(p.oid) LIKE '%stock_ready_for_trading%'
      AND NOT EXISTS(
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
  THEN RAISE EXCEPTION 'pos_site_stock_reconciliation_privacy_guard_missing'; END IF;
  -- Both inventories are forward-linked only for verified stock movement.
  -- Never allow anonymous callers to alter bridged balances or audit evidence.
  IF NOT EXISTS(SELECT 1 FROM pg_class cl
      WHERE cl.oid='pos_cashier.stock_bridge_events'::regclass
        AND cl.relrowsecurity)
    OR NOT EXISTS(SELECT 1 FROM pg_trigger t
      WHERE t.tgrelid='pos_cashier.stock_bridge_events'::regclass
        AND t.tgname='pos_stock_bridge_events_immutable' AND t.tgenabled='O')
    OR NOT EXISTS(SELECT 1 FROM pg_constraint con
      WHERE con.conrelid='pos.stock_movements'::regclass
        AND con.conname='ck_pos_stock_origin_bridge')
    OR NOT EXISTS(SELECT 1 FROM pg_trigger t
      WHERE t.tgrelid='pos.stock_movements'::regclass
        AND t.tgname='pos_a_cashier_mirror_guard' AND t.tgenabled='O')
    OR NOT EXISTS(SELECT 1 FROM pg_trigger t
      WHERE t.tgrelid='pos.stock_movements'::regclass
        AND t.tgname='pos_z_cashier_mirror_commit' AND t.tgenabled='O')
  THEN RAISE EXCEPTION 'pos_stock_bridge_immutability_missing'; END IF;
  IF EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid IN (
      'pos_cashier.guard_legacy_stock_mirror()'::regprocedure,
      'pos_cashier.commit_legacy_stock_mirror()'::regprocedure
    ) AND (NOT p.prosecdef OR EXISTS(
      SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
      WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE')))
  THEN RAISE EXCEPTION 'pos_stock_bridge_callable_publicly'; END IF;
  -- The transactional receipt issuer allows an assigned named cashier only
  -- under an active exact shift, while keeping runtime access private.
  IF NOT EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid='pos_cashier.issue_verified_internal_receipt(text,text,uuid,uuid)'::regprocedure
      AND p.prosecdef
      AND pg_get_functiondef(p.oid) LIKE '%receipt_cashier_scope_changed%'
      AND pg_get_functiondef(p.oid) LIKE '%receipt_cashier_shift_closed%'
      AND NOT EXISTS(
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
  THEN RAISE EXCEPTION 'pos_named_cashier_atomic_receipt_authority_missing'; END IF;
  -- Independent second count: private immutable record and no raw grants.
  IF NOT EXISTS(SELECT 1 FROM pg_class cl
      WHERE cl.oid='pos_cashier.shift_recovery_reviews'::regclass
        AND cl.relrowsecurity)
    OR NOT EXISTS(SELECT 1 FROM pg_trigger t
      WHERE t.tgrelid='pos_cashier.shift_recovery_reviews'::regclass
        AND t.tgname='pos_recovery_reviews_immutable' AND t.tgenabled='O')
    OR NOT EXISTS(SELECT 1 FROM pg_constraint
      WHERE conrelid='pos_cashier.transaction_audit'::regclass
        AND conname='transaction_audit_action_check'
        AND pg_get_constraintdef(oid) LIKE '%shift_recovery_review%')
  THEN RAISE EXCEPTION 'pos_second_count_immutable_evidence_missing'; END IF;
  IF EXISTS(SELECT 1 FROM pg_proc p
    WHERE p.oid IN (
      'pos_cashier.read_admin_shift_recovery_review_queue(text,text)'::regprocedure,
      'pos_cashier.review_recovered_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure
    ) AND (NOT p.prosecdef OR EXISTS(
      SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
      WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE')))
  THEN RAISE EXCEPTION 'pos_second_count_public_exposure'; END IF;
  IF (SELECT count(*) FROM pg_proc p WHERE p.oid IN (
      'pos_cashier.read_admin_shift_recovery_review_queue(text,text)'::regprocedure,
      'pos_cashier.review_recovered_cashier_shift(text,text,uuid,bigint,uuid)'::regprocedure
      ) AND p.prosecdef)<>2
  THEN RAISE EXCEPTION 'pos_second_count_functions_missing'; END IF;
  IF NOT EXISTS(SELECT 1 FROM pg_constraint
    WHERE conrelid='pos.academy_football_location_links'::regclass
      AND contype='f')
  THEN RAISE EXCEPTION 'pos_canonical_site_fk_missing'; END IF;
END $checks$;
ROLLBACK;