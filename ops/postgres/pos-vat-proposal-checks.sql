\set ON_ERROR_STOP on
-- Only disposable migrated PostgreSQL; zero real sales, credentials or ledger writes.
BEGIN;
DO $vat_checks$
DECLARE f oid;
BEGIN
  IF NOT EXISTS(
    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname='pos_cashier' AND c.relname='vat_classification_proposals'
      AND c.relrowsecurity)
  THEN RAISE EXCEPTION 'pos_vat_proposal_rls_missing'; END IF;

  IF EXISTS(SELECT 1 FROM pos_cashier.vat_classification_proposals)
    OR EXISTS(SELECT 1 FROM pos_cashier.vat_treatments WHERE enabled)
  THEN RAISE EXCEPTION 'pos_vat_policy_or_proposal_auto_enabled'; END IF;

  IF NOT EXISTS(
    SELECT 1 FROM pg_trigger
    WHERE tgrelid='pos_cashier.vat_classification_proposals'::regclass
      AND tgname='pos_vat_proposal_immutable' AND NOT tgisinternal)
  THEN RAISE EXCEPTION 'pos_vat_proposals_mutable'; END IF;
  IF NOT EXISTS(
    SELECT 1 FROM pg_class c
    WHERE c.oid='pos_cashier.vat_classification_reviews'::regclass
      AND c.relrowsecurity)
    OR NOT EXISTS(
      SELECT 1 FROM pg_trigger
      WHERE tgrelid='pos_cashier.vat_classification_reviews'::regclass
        AND tgname='pos_vat_review_immutable' AND NOT tgisinternal)
  THEN RAISE EXCEPTION 'pos_vat_reviews_not_immutable_or_private'; END IF;
  IF EXISTS(SELECT 1 FROM pos_cashier.vat_classification_reviews)
  THEN RAISE EXCEPTION 'vat_reviews_automatically_created'; END IF;

  IF EXISTS(
    SELECT 1 FROM pg_class c
    CROSS JOIN LATERAL aclexplode(
      coalesce(c.relacl,acldefault('r',c.relowner))) acl
    WHERE c.oid='pos_cashier.vat_classification_proposals'::regclass
      AND acl.grantee=0 AND acl.privilege_type IN ('SELECT','INSERT','UPDATE','DELETE'))
  THEN RAISE EXCEPTION 'pos_vat_public_table_access'; END IF;

  FOREACH f IN ARRAY ARRAY[
    'pos_cashier.read_admin_vat_proposals(text,text)'::regprocedure::oid,
    'pos_cashier.propose_admin_variant_vat(text,text,uuid,text,text,text,date,text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_vat_reviews(text,text)'::regprocedure::oid,
    'pos_cashier.read_admin_vat_assignments(text,text)'::regprocedure::oid,
    'pos_cashier.review_admin_variant_vat(text,text,uuid,boolean,text)'::regprocedure::oid
  ] LOOP
    IF NOT EXISTS(SELECT 1 FROM pg_proc p JOIN pg_language l ON l.oid=p.prolang
      WHERE p.oid=f AND p.prosecdef AND l.lanname='plpgsql'
      AND NOT EXISTS(
        SELECT 1 FROM aclexplode(coalesce(p.proacl,acldefault('f',p.proowner))) acl
        WHERE acl.grantee=0 AND acl.privilege_type='EXECUTE'))
    THEN RAISE EXCEPTION 'pos_vat_public_function_access_or_unsafe_definer'; END IF;
  END LOOP;

  BEGIN
    PERFORM pos_cashier.read_admin_vat_proposals(
      repeat('0',64),'lefteria_fc');
    RAISE EXCEPTION 'unauthenticated_vat_read_allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  BEGIN
    PERFORM pos_cashier.read_admin_vat_assignments(
      repeat('0',64),'lefteria_fc');
    RAISE EXCEPTION 'unauthenticated_vat_assignments_allowed';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  BEGIN
    PERFORM pos_cashier.propose_admin_variant_vat(
      repeat('0',64),'lefteria_fc',gen_random_uuid(),
      'no_such_product','no_such_variant','standard_19',
      DATE '2026-11-22','Synthetic authority test only',
      'Synthetic justification test only');
    RAISE EXCEPTION 'unauthenticated_vat_write_allowed';
  EXCEPTION
    WHEN insufficient_privilege THEN NULL;
    WHEN raise_exception THEN
      IF SQLERRM <> 'admin_club_session_lock_missing' THEN RAISE; END IF;
  END;

  BEGIN
    PERFORM pos_cashier.review_admin_variant_vat(
      repeat('0',64),'lefteria_fc',gen_random_uuid(),true,
      'Synthetic review denial check');
    RAISE EXCEPTION 'unauthenticated_vat_review_allowed';
  EXCEPTION
    WHEN insufficient_privilege THEN NULL;
    WHEN raise_exception THEN
      IF SQLERRM <> 'admin_club_session_lock_missing' THEN RAISE; END IF;
  END;

  IF EXISTS(SELECT 1 FROM pos_cashier.vat_classification_proposals)
  THEN RAISE EXCEPTION 'vat_negative_test_mutated_data'; END IF;
END $vat_checks$;
ROLLBACK;
