\set ON_ERROR_STOP on
\set ECHO none
-- Synthetic acceptance for Academy POS site/terminal/finance authority.
-- Run ONLY against disposable migrated PostgreSQL. One unpaid synthetic
-- sale exists only in a rollback-on-error subtransaction; all changes ROLLBACK.
-- No verified payment, refund, receipt, Payabl request or real person exists.
BEGIN;
DO $site_scope$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_second_super uuid:=gen_random_uuid();
  v_second_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_second_request uuid:=gen_random_uuid();
  v_regular_shift uuid;
  v_disputed_shift uuid;
  v_city_manager uuid:=gen_random_uuid();
  v_local_manager uuid:=gen_random_uuid();
  v_neighbor_manager uuid:=gen_random_uuid();
  v_unscoped uuid:=gen_random_uuid();
  v_team_manager uuid:=gen_random_uuid();
  v_cashier uuid:=gen_random_uuid();
  v_cashier_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_cashier_shift uuid;
  v_recovery_request uuid:=gen_random_uuid();
  v_pending_sale uuid:=gen_random_uuid();
  v_checkout_product text;
  v_checkout_category text;
  v_checkout_sale uuid;
  v_checkout_receipt uuid;
  v_checkout_request uuid:=gen_random_uuid();
  v_cash_request uuid:=gen_random_uuid();
  v_stock_receipt_request uuid:=gen_random_uuid();
  v_cash_read jsonb;
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_city_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_local_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_neighbor_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_unscoped_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_team_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_city_a uuid:=gen_random_uuid();
  v_city_b uuid:=gen_random_uuid();
  v_site_a1 uuid:=gen_random_uuid();
  v_site_a2 uuid:=gen_random_uuid();
  v_site_b1 uuid:=gen_random_uuid();
  v_scope_a uuid:=gen_random_uuid();
  v_scope_b uuid:=gen_random_uuid();
  v_pos_a1 text;
  v_pos_a2 text;
  v_pos_b1 text;
  v_register_a1 text;
  v_register_a2 text;
  v_register_b1 text;
  v_result jsonb;
  v_denied boolean;
BEGIN
  -- The owner is present in the disposable integration seed. Names, IDs and
  -- tokens belong to this transaction and never leave the test database.
  INSERT INTO identity.admin_accounts
    (id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES
    (v_super,'ci-pos-super-'||left(md5(v_super::text),8),
      'CI POS Super Admin','super_admin',true,'ci-fixture-hash',false,false),
    (v_second_super,'ci-pos-second-super-'||left(md5(v_second_super::text),8),
      'CI POS Second Super Admin','super_admin',true,'ci-fixture-hash',false,false),
    (v_city_manager,'ci-pos-city-'||left(md5(v_city_manager::text),8),
      'CI POS City Manager','manager',true,'ci-fixture-hash',false,false),
    (v_local_manager,'ci-pos-local-'||left(md5(v_local_manager::text),8),
      'CI POS Location Manager','manager',true,'ci-fixture-hash',false,false),
    (v_neighbor_manager,'ci-pos-neighbor-'||left(md5(v_neighbor_manager::text),8),
      'CI POS Other City','manager',true,'ci-fixture-hash',false,false),
    (v_unscoped,'ci-pos-unscoped-'||left(md5(v_unscoped::text),8),
      'CI POS No Site','manager',true,'ci-fixture-hash',false,false),
    (v_team_manager,'ci-pos-team-'||left(md5(v_team_manager::text),8),
      'CI POS First Team','manager',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants
    (admin_account_id,organization_id)
  VALUES
    (v_super,'lefteria_fc'),(v_super,'lefteria_fc_academy'),
    (v_second_super,'lefteria_fc_academy'),
    (v_city_manager,'lefteria_fc_academy'),
    (v_local_manager,'lefteria_fc_academy'),
    (v_neighbor_manager,'lefteria_fc_academy'),
    (v_unscoped,'lefteria_fc_academy'),
    (v_team_manager,'lefteria_fc');
  INSERT INTO identity.admin_sessions
    (id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES
    (gen_random_uuid(),v_super_token,v_super,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_second_super_token,v_second_super,repeat('9',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_city_token,v_city_manager,repeat('b',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_local_token,v_local_manager,repeat('c',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_neighbor_token,v_neighbor_manager,repeat('d',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_unscoped_token,v_unscoped,repeat('e',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_team_token,v_team_manager,repeat('f',64),
      clock_timestamp(),clock_timestamp()+interval '1 day');

  INSERT INTO club.football_cities(id,name,slug)
  VALUES (v_city_a,'CI POS Alpha','ci-pos-alpha-'||left(md5(v_city_a::text),8)),
    (v_city_b,'CI POS Beta','ci-pos-beta-'||left(md5(v_city_b::text),8));
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
  VALUES (v_scope_a,'lefteria_fc_academy',v_city_a),
    (v_scope_b,'lefteria_fc_academy',v_city_b);
  INSERT INTO club.football_locations(id,city_id,name,slug)
  VALUES (v_site_a1,v_city_a,'CI POS Alpha North','north'),
    (v_site_a2,v_city_a,'CI POS Alpha South','south'),
    (v_site_b1,v_city_b,'CI POS Beta East','east');
  INSERT INTO club.football_city_admin_grants(admin_account_id,city_id,role)
  VALUES(v_city_manager,v_city_a,'city_manager');
  INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role)
  VALUES(v_local_manager,v_site_a1,'location_manager'),
    (v_neighbor_manager,v_site_b1,'location_manager');

  -- Only the owner may provision; the city and location must have a canonical
  -- active football location. IDs are deterministic, not client-supplied.
  v_result:=pos_cashier.ensure_admin_academy_pos_site(v_super_token,v_site_a1);
  v_pos_a1:=v_result->>'pos_location_id';
  IF v_result->>'organization_id'<>'lefteria_fc_academy'
    OR (v_result->>'academy_city_id')::uuid<>v_city_a
    OR (v_result->>'academy_location_id')::uuid<>v_site_a1
  THEN RAISE EXCEPTION 'pos_canonical_north_site_not_bound'; END IF;
  v_pos_a2:=pos_cashier.ensure_admin_academy_pos_site(
    v_super_token,v_site_a2)->>'pos_location_id';
  v_pos_b1:=pos_cashier.ensure_admin_academy_pos_site(
    v_super_token,v_site_b1)->>'pos_location_id';
  IF v_pos_a1 IS NULL OR v_pos_a2 IS NULL OR v_pos_b1 IS NULL
    OR v_pos_a1=v_pos_a2 OR v_pos_a1=v_pos_b1
  THEN RAISE EXCEPTION 'pos_canonical_site_identity_collision'; END IF;
  IF (SELECT count(*) FROM pos.academy_football_location_links
      WHERE pos_location_id IN (v_pos_a1,v_pos_a2,v_pos_b1))<>3
  THEN RAISE EXCEPTION 'pos_canonical_link_missing'; END IF;
  v_result:=pos_cashier.ensure_admin_academy_pos_site(v_super_token,v_site_a1);
  IF v_result->>'pos_location_id'<>v_pos_a1
    OR (SELECT count(*) FROM pos.academy_football_location_links
      WHERE academy_location_id=v_site_a1)<>1
  THEN RAISE EXCEPTION 'pos_canonical_site_ensure_not_idempotent'; END IF;
  BEGIN
    PERFORM pos_cashier.ensure_admin_academy_pos_site(v_city_token,v_site_a1);
    RAISE EXCEPTION 'city_manager_provisioned_pos_canonical_site';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Three physical registers, one per exact site, share no permissions.
  v_register_a1:=pos_cashier.create_admin_terminal(
    v_super_token,'lefteria_fc_academy',v_pos_a1,'CI Register North')->>'register_id';
  v_register_a2:=pos_cashier.create_admin_terminal(
    v_super_token,'lefteria_fc_academy',v_pos_a2,'CI Register South')->>'register_id';
  v_register_b1:=pos_cashier.create_admin_terminal(
    v_super_token,'lefteria_fc_academy',v_pos_b1,'CI Register East')->>'register_id';
  IF (SELECT count(*) FROM pos_cashier.receipt_sequences
    WHERE organization_id='lefteria_fc_academy'
      AND register_id IN (v_register_a1,v_register_a2,v_register_b1))<>3
  THEN RAISE EXCEPTION 'pos_register_receipt_sequences_not_provisioned'; END IF;
  BEGIN
    PERFORM pos_cashier.create_admin_terminal(
      v_city_token,'lefteria_fc_academy',v_pos_a1,'Forbidden Register');
    RAISE EXCEPTION 'city_manager_provisioned_register';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  v_result:=pos_cashier.set_admin_terminal_operator(
    v_super_token,'lefteria_fc_academy',v_register_a1,v_local_manager,true);
  IF NOT (v_result->>'active')::boolean
    OR v_result->>'register_id'<>v_register_a1
  THEN RAISE EXCEPTION 'authorized_location_operator_assignment_failed'; END IF;
  BEGIN
    PERFORM pos_cashier.set_admin_terminal_operator(
      v_super_token,'lefteria_fc_academy',v_register_a2,v_local_manager,true);
    RAISE EXCEPTION 'north_operator_assigned_to_sibling_location';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.set_admin_terminal_operator(
      v_super_token,'lefteria_fc_academy',v_register_a1,v_neighbor_manager,true);
    RAISE EXCEPTION 'other_city_operator_assigned_to_north';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.set_admin_terminal_operator(
      v_local_token,'lefteria_fc_academy',v_register_a1,v_local_manager,true);
    RAISE EXCEPTION 'location_manager_granted_new_cashier_authority';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF (SELECT count(*) FROM pos_cashier.cashier_assignments
      WHERE organization_id='lefteria_fc_academy'
        AND register_id IN (v_register_a1,v_register_a2,v_register_b1)
        AND is_active)<>1
  THEN RAISE EXCEPTION 'failed_cross_site_assignment_was_persisted'; END IF;
  -- Cashier is an ordinary named credential but carries NO city-manager
  -- grant and no Academy, finance, staff or catalog rights.
  INSERT INTO identity.admin_accounts
    (id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES(v_cashier,'ci-pos-cashier-'||left(md5(v_cashier::text),8),
    'CI Terminal Cashier','cashier',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(v_cashier,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions
    (id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
    VALUES(gen_random_uuid(),v_cashier_token,v_cashier,repeat('f',64),
      clock_timestamp(),clock_timestamp()+interval '1 day');
  v_result:=pos_cashier.set_admin_terminal_operator(
    v_super_token,'lefteria_fc_academy',v_register_a1,v_cashier,true);
  IF v_result->>'admin_account_id'<>v_cashier::text
     OR NOT (v_result->>'active')::boolean
  THEN RAISE EXCEPTION 'cashier_single_register_assignment_failed'; END IF;
  IF pos_cashier.current_cashier_authority(
    v_cashier_token,'lefteria_fc_academy',v_register_a1)<>v_cashier
  THEN RAISE EXCEPTION 'cashier_assigned_register_rejected'; END IF;
  v_result:=pos_cashier.read_admin_cashier_registers(
    v_cashier_token,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'registers')<>1
    OR v_result->'registers'->0->>'id'<>v_register_a1
  THEN RAISE EXCEPTION 'cashier_discovered_other_location'; END IF;
  BEGIN
    PERFORM pos_cashier.set_admin_terminal_operator(
      v_super_token,'lefteria_fc_academy',v_register_a2,v_cashier,true);
    RAISE EXCEPTION 'cashier_assigned_second_active_register';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.current_cashier_authority(
      v_cashier_token,'lefteria_fc_academy',v_register_b1);
    RAISE EXCEPTION 'cashier_cross_city_register_access';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.authorize_site_finances(
      v_cashier_token,'lefteria_fc_academy',v_pos_a1);
    RAISE EXCEPTION 'cashier_read_location_finance';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_sites(
      v_cashier_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'cashier_read_management_site_directory';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- A real shift opens and closes without inventing any sale or receipt.
  v_result:=pos_cashier.open_admin_cashier_shift(
    v_cashier_token,'lefteria_fc_academy',v_register_a1,1200,gen_random_uuid());
  v_cashier_shift:=(v_result->>'shift_id')::uuid;
  IF v_cashier_shift IS NULL
  THEN RAISE EXCEPTION 'cashier_cannot_open_assigned_shift'; END IF;
  -- Disposable, rollback-on-error acceptance: the named Cashier, not just a
  -- manager, must be able to prepare an exact server-priced sale, tender
  -- physically counted cash, issue the same atomic receipt, consume stock
  -- once and reconcile the shift. Synthetic test policy is NEVER persisted.
  -- The actual policies and function grants remain disabled for real users.
  BEGIN
    BEGIN
      PERFORM pos_cashier.prepare_cashier_checkout(
        v_cashier_token,'lefteria_fc_academy',v_register_a1,
        v_cashier_shift,gen_random_uuid(),
        '[{"product_id":"not-configured","variant_id":"unit","quantity":1}]'::jsonb);
      RAISE EXCEPTION 'unapproved_checkout_was_enabled';
    EXCEPTION WHEN SQLSTATE '55000' THEN NULL; END;

    v_checkout_category:='ci-category-'||left(md5(v_cashier::text),12);
    v_checkout_product:='ci-product-'||left(md5(v_cashier::text),12);
    INSERT INTO pos.categories(organization_id,id,name)
      VALUES('lefteria_fc_academy',v_checkout_category,'CI isolated product category');
    INSERT INTO pos.products(organization_id,id,category_id,name)
      VALUES('lefteria_fc_academy',v_checkout_product,v_checkout_category,
        'CI isolated cash-only item');
    INSERT INTO pos.product_variants(
      organization_id,product_id,id,name,price_cents,cost_cents)
    VALUES('lefteria_fc_academy',v_checkout_product,'unit','One',250,100);
    INSERT INTO pos_cashier.stock(
      organization_id,location_id,product_id,variant_id,on_hand)
    VALUES('lefteria_fc_academy',v_pos_a1,v_checkout_product,'unit',3);
    -- The two inventories are intentionally separate today. The site
    -- manager can diagnose missing ledger entries without writing either.
    v_result:=pos_cashier.read_admin_site_stock_reconciliation(
      v_local_token,'lefteria_fc_academy',v_pos_a1);
    IF (v_result->>'total_skus')::integer<>1
      OR (v_result->>'legacy_missing_skus')::integer<>1
      OR (v_result->>'discrepant_skus')::integer<>1
      OR (v_result->>'ledger_balances_match')::boolean
      OR (v_result->>'physical_count_approved')::boolean
      OR (v_result->>'stock_ready_for_trading')::boolean
    THEN RAISE EXCEPTION 'pos_stock_missing_legacy_ledger_not_reported'; END IF;

    BEGIN
      PERFORM pos_cashier.read_admin_site_stock_reconciliation(
        v_local_token,'lefteria_fc_academy',v_pos_a2);
      RAISE EXCEPTION 'pos_manager_read_other_location_inventory';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
      PERFORM pos_cashier.read_admin_site_stock_reconciliation(
        v_neighbor_token,'lefteria_fc_academy',v_pos_a1);
      RAISE EXCEPTION 'pos_other_city_manager_read_inventory';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
      PERFORM pos_cashier.read_admin_site_stock_reconciliation(
        v_cashier_token,'lefteria_fc_academy',v_pos_a1);
      RAISE EXCEPTION 'pos_cashier_read_site_inventory_reconciliation';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;

    -- Independent legacy product assignment and receiving evidence.
    -- Synthetic only: these rows are rolled back with the nested block.
    INSERT INTO pos.location_products(
      organization_id,location_id,product_id,enabled)
    VALUES('lefteria_fc_academy',v_pos_a1,v_checkout_product,true);
    INSERT INTO pos.location_stock(
      organization_id,location_id,product_id,variant_id,on_hand)
    VALUES('lefteria_fc_academy',v_pos_a1,v_checkout_product,'unit',3);
    v_result:=pos_cashier.read_admin_site_stock_reconciliation(
      v_local_token,'lefteria_fc_academy',v_pos_a1);
    IF (v_result->>'matched_skus')::integer<>1
      OR (v_result->>'discrepant_skus')::integer<>0
      OR NOT (v_result->>'ledger_balances_match')::boolean
      OR (v_result->>'stock_ready_for_trading')::boolean
    THEN RAISE EXCEPTION 'pos_stock_balances_match_wrong_or_approved'; END IF;

    v_result:=pos_cashier.read_assigned_cashier_sellable_stock(
      v_cashier_token,'lefteria_fc_academy',v_register_a1,v_cashier_shift);
    IF v_result->>'organization_id'<>'lefteria_fc_academy'
      OR v_result->>'location_id'<>v_pos_a1
      OR (v_result->>'fiscal_policies_approved')::boolean
      OR jsonb_array_length(v_result->'items')<>1
      OR (v_result->'items'->0->>'available')::integer<>3
      OR (v_result->'items'->0->>'price_cents')::bigint<>250
      OR (v_result->'items'->0 ? 'cost_cents')
    THEN RAISE EXCEPTION 'cashier_stock_read_is_not_private_or_policy_gated'; END IF;
    BEGIN
      PERFORM pos_cashier.read_assigned_cashier_sellable_stock(
        v_cashier_token,'lefteria_fc_academy',v_register_b1,v_cashier_shift);
      RAISE EXCEPTION 'cashier_read_other_city_stock';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
      PERFORM pos_cashier.read_assigned_cashier_sellable_stock(
        v_local_token,'lefteria_fc_academy',v_register_a1,v_cashier_shift);
      RAISE EXCEPTION 'manager_borrowed_other_cashier_shift_stock';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN
      PERFORM pos_cashier.read_assigned_cashier_sellable_stock(
        v_cashier_token,'lefteria_fc',v_register_a1,v_cashier_shift);
      RAISE EXCEPTION 'cashier_read_other_organization_stock';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;

    INSERT INTO pos_cashier.checkout_policies(
      organization_id,enabled,tax_treatment,reviewed_by,reviewed_at,policy_reference)
    VALUES('lefteria_fc_academy',true,'verified_exempt',v_super,
      clock_timestamp()-interval '1 minute','CI synthetic only: never production');
    INSERT INTO pos_cashier.receipt_policies(
      organization_id,enabled,policy_version,policy_sha256,reviewed_by,reviewed_at)
    VALUES('lefteria_fc_academy',true,'ci-disposable-only',
      repeat('a',64),v_super,clock_timestamp()-interval '1 minute');

    v_result:=pos_cashier.read_assigned_cashier_sellable_stock(
      v_cashier_token,'lefteria_fc_academy',v_register_a1,v_cashier_shift);
    IF NOT (v_result->>'fiscal_policies_approved')::boolean
    THEN RAISE EXCEPTION 'cashier_stock_fiscal_approval_not_reported'; END IF;

    BEGIN
      PERFORM pos_cashier.prepare_cashier_checkout(
        v_local_token,'lefteria_fc_academy',v_register_a1,
        v_cashier_shift,gen_random_uuid(),
        jsonb_build_array(jsonb_build_object('product_id',v_checkout_product,
          'variant_id','unit','quantity',2)));
      RAISE EXCEPTION 'different_operator_prepared_cashier_shift_sale';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    v_result:=pos_cashier.prepare_cashier_checkout(
      v_cashier_token,'lefteria_fc_academy',v_register_a1,
      v_cashier_shift,v_checkout_request,
      jsonb_build_array(jsonb_build_object('product_id',v_checkout_product,
        'variant_id','unit','quantity',2)));
    v_checkout_sale:=(v_result->>'sale_id')::uuid;
    IF (v_result->>'total_cents')::bigint<>500
      OR v_result->>'status'<>'awaiting_payment'
      OR (SELECT reserved FROM pos_cashier.stock WHERE
          organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit')<>2
      OR (SELECT on_hand FROM pos_cashier.stock WHERE
          organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit')<>3
    THEN RAISE EXCEPTION 'cashier_prepare_stock_reservation_invalid'; END IF;

    v_result:=pos_cashier.prepare_cashier_checkout(
      v_cashier_token,'lefteria_fc_academy',v_register_a1,
      v_cashier_shift,v_checkout_request,
      jsonb_build_array(jsonb_build_object('product_id',v_checkout_product,
        'variant_id','unit','quantity',2)));
    IF NOT (v_result->>'replay')::boolean
      OR (v_result->>'sale_id')::uuid<>v_checkout_sale
    THEN RAISE EXCEPTION 'cashier_prepare_replay_not_idempotent'; END IF;
    BEGIN
      PERFORM pos_cashier.prepare_cashier_checkout(
        v_cashier_token,'lefteria_fc_academy',v_register_a1,
        v_cashier_shift,v_checkout_request,
        jsonb_build_array(jsonb_build_object('product_id',v_checkout_product,
          'variant_id','unit','quantity',1)));
      RAISE EXCEPTION 'cashier_prepare_replay_changed_quantity';
    EXCEPTION WHEN unique_violation THEN NULL; END;

    -- A stale/incorrect legacy balance MUST abort the complete cash,
    -- numbered receipt and stock transaction without any verified payment.
    BEGIN
      UPDATE pos.location_stock SET on_hand=4
        WHERE organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit';
      BEGIN
        PERFORM pos_cashier.settle_exact_cash_sale(
          v_cashier_token,'lefteria_fc_academy',v_checkout_sale,
          v_cash_request,700);
        RAISE EXCEPTION 'cash_sale_paid_with_diverged_legacy_stock';
      EXCEPTION WHEN unique_violation THEN NULL; END;
      IF EXISTS(SELECT 1 FROM pos_cashier.payment_events
          WHERE sale_id=v_checkout_sale)
        OR EXISTS(SELECT 1 FROM pos_cashier.receipts
          WHERE sale_id=v_checkout_sale)
      THEN RAISE EXCEPTION 'diverged_cash_tender_left_partial_payment'; END IF;
      RAISE EXCEPTION 'pos_diverged_stock_test_rolled_back' USING ERRCODE='P0001';
    EXCEPTION WHEN SQLSTATE 'P0001' THEN
      IF SQLERRM<>'pos_diverged_stock_test_rolled_back' THEN RAISE; END IF;
    END;

    v_result:=pos_cashier.settle_exact_cash_sale(
      v_cashier_token,'lefteria_fc_academy',v_checkout_sale,v_cash_request,700);
    v_checkout_receipt:=(v_result->>'receipt_id')::uuid;
    IF v_checkout_receipt IS NULL OR (v_result->>'total_cents')::bigint<>500
      OR (v_result->>'tendered_cents')::bigint<>700
      OR (v_result->>'change_cents')::bigint<>200
      OR (v_result->>'replay')::boolean
      OR (SELECT status FROM pos_cashier.sales WHERE id=v_checkout_sale)<>'paid'
      OR (SELECT on_hand FROM pos_cashier.stock WHERE
          organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit')<>1
      OR (SELECT reserved FROM pos_cashier.stock WHERE
          organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit')<>0
      OR (SELECT count(*) FROM pos_cashier.sale_reservations
          WHERE sale_id=v_checkout_sale AND consumed_at IS NOT NULL)<>1
    THEN RAISE EXCEPTION 'cashier_atomic_tender_receipt_stock_invalid'; END IF;

    -- An actual paid cashier receipt now writes an immutable legacy
    -- cashier_sale movement and consumes BOTH ledgers in ONE transaction.
    v_result:=pos_cashier.read_admin_site_stock_reconciliation(
      v_local_token,'lefteria_fc_academy',v_pos_a1);
    IF (v_result->>'discrepant_skus')::integer<>0
      OR (v_result->'lines'->0->>'status')<>'matched'
      OR NOT (v_result->>'ledger_balances_match')::boolean
      OR (v_result->>'stock_ready_for_trading')::boolean
      OR (SELECT on_hand FROM pos.location_stock
        WHERE organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit')<>1
      OR (SELECT count(*) FROM pos.stock_movements
        WHERE cashier_sale_id=v_checkout_sale AND reason='cashier_sale'
          AND delta=-2)<>1
      OR (SELECT count(*) FROM pos_cashier.stock_bridge_events e
        JOIN pos.stock_movements m ON m.id=e.movement_id
        WHERE m.cashier_sale_id=v_checkout_sale)<>1
    THEN RAISE EXCEPTION 'pos_completed_sale_not_atomically_mirrored'; END IF;

    -- A reviewed purchase receipt uses the old immutable POS movement and
    -- now automatically increments cashier stock by the SAME amount.
    v_result:=pos.receive_admin_stock(
      v_super_token,'lefteria_fc_academy',v_pos_a1,
      v_checkout_product,'unit',2,v_stock_receipt_request);
    IF (v_result->>'after_quantity')::bigint<>3 OR
      (v_result->>'replay')::boolean
      OR (SELECT on_hand FROM pos.location_stock
        WHERE organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit')<>3
      OR (SELECT on_hand FROM pos_cashier.stock
        WHERE organization_id='lefteria_fc_academy' AND location_id=v_pos_a1
          AND product_id=v_checkout_product AND variant_id='unit')<>3
      OR (SELECT count(*) FROM pos_cashier.stock_bridge_events e
          WHERE e.organization_id='lefteria_fc_academy'
            AND e.location_id=v_pos_a1 AND e.product_id=v_checkout_product)<>2
    THEN RAISE EXCEPTION 'pos_purchase_did_not_mirror_both_ledgers'; END IF;
    v_result:=pos.receive_admin_stock(
      v_super_token,'lefteria_fc_academy',v_pos_a1,
      v_checkout_product,'unit',2,v_stock_receipt_request);
    IF NOT (v_result->>'replay')::boolean OR
      (SELECT count(*) FROM pos_cashier.stock_bridge_events e
          WHERE e.organization_id='lefteria_fc_academy'
            AND e.location_id=v_pos_a1 AND e.product_id=v_checkout_product)<>2
    THEN RAISE EXCEPTION 'pos_purchase_replay_doubled_inventory'; END IF;
    v_result:=pos_cashier.read_admin_site_stock_reconciliation(
      v_local_token,'lefteria_fc_academy',v_pos_a1);
    IF (v_result->>'discrepant_skus')::integer<>0
      OR (v_result->'lines'->0->>'cashier_on_hand')::bigint<>3
      OR (v_result->'lines'->0->>'legacy_on_hand')::bigint<>3
    THEN RAISE EXCEPTION 'pos_post_receiving_inventory_not_reconciled'; END IF;

    v_cash_read:=pos_cashier.read_assigned_cashier_receipt(
      v_cashier_token,'lefteria_fc_academy',v_checkout_receipt);
    IF v_cash_read->>'sale_id'<>v_checkout_sale::text
      OR (v_cash_read->'receipt'->>'total_cents')::bigint<>500
      OR jsonb_array_length(v_cash_read->'receipt'->'lines')<>1
    THEN RAISE EXCEPTION 'cashier_own_receipt_not_readable_or_incomplete'; END IF;
    BEGIN
      PERFORM pos_cashier.read_assigned_cashier_receipt(
        v_local_token,'lefteria_fc_academy',v_checkout_receipt);
      RAISE EXCEPTION 'manager_read_other_cashier_original_receipt';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    v_result:=pos_cashier.settle_exact_cash_sale(
      v_cashier_token,'lefteria_fc_academy',v_checkout_sale,v_cash_request,700);
    IF NOT (v_result->>'replay')::boolean
      OR (SELECT count(*) FROM pos_cashier.payment_events
          WHERE sale_id=v_checkout_sale AND status='verified')<>1
      OR (SELECT count(*) FROM pos_cashier.receipts
          WHERE sale_id=v_checkout_sale)<>1
    THEN RAISE EXCEPTION 'cashier_tender_replayed_duplicate_payment_or_receipt'; END IF;
    BEGIN
      PERFORM pos_cashier.settle_exact_cash_sale(
        v_cashier_token,'lefteria_fc_academy',v_checkout_sale,v_cash_request,800);
      RAISE EXCEPTION 'cashier_tender_replay_changed_cash_amount';
    EXCEPTION WHEN unique_violation THEN NULL; END;
    v_result:=pos_cashier.read_cashier_shift_reconciliation(
      v_cashier_token,'lefteria_fc_academy',v_cashier_shift);
    IF (v_result->>'cash_verified_cents')::bigint<>500
      OR (v_result->>'expected_cash_cents')::bigint<>1700
      OR (v_result->>'receipts_issued')::integer<>1
      OR (v_result->>'active_reservations')::integer<>0
    THEN RAISE EXCEPTION 'cashier_paid_shift_reconciliation_invalid'; END IF;

    -- Deliberately abort the entire synthetic sale/payments/receipt/policies
    -- subtransaction. Real checkout grants or fiscal policy never persist.
    RAISE EXCEPTION 'pos_synthetic_cash_sale_rolled_back' USING ERRCODE='P0001';
  EXCEPTION WHEN SQLSTATE 'P0001' THEN
    IF SQLERRM<>'pos_synthetic_cash_sale_rolled_back' THEN RAISE; END IF;
  END;
  IF EXISTS(SELECT 1 FROM pos_cashier.checkout_policies
      WHERE organization_id='lefteria_fc_academy')
    OR EXISTS(SELECT 1 FROM pos_cashier.receipt_policies
      WHERE organization_id='lefteria_fc_academy')
    OR EXISTS(SELECT 1 FROM pos_cashier.payment_events
      WHERE sale_id=v_checkout_sale)
    OR EXISTS(SELECT 1 FROM pos_cashier.receipts
      WHERE sale_id=v_checkout_sale)
  THEN RAISE EXCEPTION 'synthetic_sale_or_policy_leaked_past_rollback'; END IF;

  v_result:=pos_cashier.close_admin_cashier_shift(
    v_cashier_token,'lefteria_fc_academy',v_cashier_shift,1200,gen_random_uuid());
  IF (v_result->>'variance_cents')::bigint<>0
  THEN RAISE EXCEPTION 'cashier_empty_shift_reconciliation_invalid'; END IF;

  -- A suspended-only recovery cannot force-close a live cashier's shift.
  v_cashier_shift:=(pos_cashier.open_admin_cashier_shift(
    v_cashier_token,'lefteria_fc_academy',v_register_a1,1200,gen_random_uuid())
      ->>'shift_id')::uuid;
  BEGIN
    PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
      v_super_token,'lefteria_fc_academy',v_cashier_shift,
      1200,v_recovery_request,'account_suspended');
    RAISE EXCEPTION 'active_cashier_shift_force_closed_by_super_admin';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
      v_city_token,'lefteria_fc_academy',v_cashier_shift,
      1200,v_recovery_request,'account_suspended');
    RAISE EXCEPTION 'city_manager_force_closed_cashier_shift';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Suspending the cashier blocks further terminal use immediately.
  UPDATE identity.admin_accounts SET is_active=false WHERE id=v_cashier;
  BEGIN
    PERFORM pos_cashier.current_cashier_authority(
      v_cashier_token,'lefteria_fc_academy',v_register_a1);
    RAISE EXCEPTION 'suspended_cashier_retained_terminal_access';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Super Admin sees exactly the suspended cashier's still-open shift.
  v_result:=pos_cashier.read_admin_terminal_setup(
    v_super_token,'lefteria_fc_academy');
  IF NOT EXISTS(
    SELECT 1 FROM jsonb_array_elements(v_result->'terminals') AS t
    WHERE t->>'id'=v_register_a1
      AND t->'open_shift'->>'id'=v_cashier_shift::text
      AND (t->'open_shift'->>'cashier_active')::boolean=false
  ) THEN RAISE EXCEPTION 'supervisor_missing_suspended_shift_preview'; END IF;
  -- A real unpaid sale blocks emergency closure. The PL/pgSQL exception
  -- block is an atomic subtransaction: on the expected 55000, it rolls back
  -- the synthetic INSERT. Ledger sales have immutable no-DELETE triggers.
  BEGIN
    INSERT INTO pos_cashier.sales(
      id,organization_id,register_id,shift_id,cashier_id,
      request_id,subtotal_cents,total_cents)
    VALUES(v_pending_sale,'lefteria_fc_academy',v_register_a1,
      v_cashier_shift,v_cashier,gen_random_uuid(),100,100);
    PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
      v_super_token,'lefteria_fc_academy',v_cashier_shift,
      1190,v_recovery_request,'account_suspended');
    RAISE EXCEPTION 'supervisor_recovered_shift_with_pending_sale';
  EXCEPTION WHEN SQLSTATE '55000' THEN NULL; END;
  IF EXISTS(SELECT 1 FROM pos_cashier.sales WHERE id=v_pending_sale)
  THEN RAISE EXCEPTION 'synthetic_pending_sale_subtransaction_not_rolled_back'; END IF;

  BEGIN
    PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
      v_cashier_token,'lefteria_fc_academy',v_cashier_shift,
      1190,v_recovery_request,'account_suspended');
    RAISE EXCEPTION 'cashier_force_closed_own_suspended_shift';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
      v_super_token,'lefteria_fc',v_cashier_shift,
      1190,v_recovery_request,'account_suspended');
    RAISE EXCEPTION 'supervisor_cross_org_shift_recovery';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Enter an actual counted cash amount; the variance is preserved,
  -- not manufactured as zero and no sale/payment/receipt is generated.
  v_result:=pos_cashier.recover_suspended_admin_cashier_shift(
    v_super_token,'lefteria_fc_academy',v_cashier_shift,
    1190,v_recovery_request,'account_suspended');
  IF (v_result->>'variance_cents')::bigint<>-10
    OR (v_result->>'expected_cash_cents')::bigint<>1200
    OR (v_result->>'counted_cash_cents')::bigint<>1190
    OR NOT (v_result->>'requires_review')::boolean
    OR (v_result->>'replay')::boolean
    OR (SELECT cashier_id FROM pos_cashier.shifts WHERE id=v_cashier_shift)<>v_cashier
    OR (SELECT recovery_actor_id FROM pos_cashier.shifts WHERE id=v_cashier_shift)<>v_super
    OR (SELECT recovery_reason FROM pos_cashier.shifts WHERE id=v_cashier_shift)
      <>'account_suspended'
  THEN RAISE EXCEPTION 'supervisor_recovery_ledger_evidence_invalid'; END IF;
  v_result:=pos_cashier.recover_suspended_admin_cashier_shift(
    v_super_token,'lefteria_fc_academy',v_cashier_shift,
    1190,v_recovery_request,'account_suspended');
  IF NOT (v_result->>'replay')::boolean
    OR (v_result->>'variance_cents')::bigint<>-10
    OR (SELECT count(*) FROM pos_cashier.transaction_audit
      WHERE organization_id='lefteria_fc_academy'
        AND request_id=v_recovery_request AND action='shift_recovery_close')<>1
  THEN RAISE EXCEPTION 'supervisor_recovery_replay_not_idempotent'; END IF;
  BEGIN
    PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
      v_super_token,'lefteria_fc_academy',v_cashier_shift,
      1200,v_recovery_request,'account_suspended');
    RAISE EXCEPTION 'supervisor_recovery_replay_count_mismatch_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  IF EXISTS(SELECT 1 FROM pos_cashier.sales
      WHERE organization_id='lefteria_fc_academy' AND shift_id=v_cashier_shift)
    OR EXISTS(SELECT 1 FROM pos_cashier.receipts WHERE organization_id='lefteria_fc_academy')
    OR EXISTS(SELECT 1 FROM pos_cashier.payment_events WHERE organization_id='lefteria_fc_academy')
  THEN RAISE EXCEPTION 'supervisor_recovery_fabricated_payment_or_receipt'; END IF;

  -- The next shift cannot begin while a recovered shift lacks an
  -- independent physical count. Even an already assigned manager is blocked.
  BEGIN
    PERFORM pos_cashier.open_admin_cashier_shift(
      v_local_token,'lefteria_fc_academy',v_register_a1,500,gen_random_uuid());
    RAISE EXCEPTION 'unreviewed_recovery_allowed_new_shift';
  EXCEPTION WHEN SQLSTATE '55000' THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.set_admin_terminal_operator(
      v_super_token,'lefteria_fc_academy',v_register_a1,v_local_manager,true);
    RAISE EXCEPTION 'unreviewed_recovery_allowed_operator_enable';
  EXCEPTION WHEN SQLSTATE '55000' THEN NULL; END;

  v_result:=pos_cashier.read_admin_shift_recovery_review_queue(
    v_second_super_token,'lefteria_fc_academy');
  IF NOT EXISTS(
    SELECT 1 FROM jsonb_array_elements(v_result->'shifts') AS q
    WHERE q->>'shift_id'=v_cashier_shift::text
      AND q->>'disposition' IS NULL)
    OR v_result::text LIKE '%first_count_cents%'
    OR v_result::text LIKE '%expected_cash_cents%'
  THEN RAISE EXCEPTION 'second_count_queue_exposes_or_misses_initial_count'; END IF;
  BEGIN
    PERFORM pos_cashier.read_admin_shift_recovery_review_queue(
      v_city_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'city_manager_read_supervisor_queue';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.review_recovered_cashier_shift(
      v_super_token,'lefteria_fc_academy',v_cashier_shift,1190,v_second_request);
    RAISE EXCEPTION 'first_supervisor_approved_own_cash_count';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.review_recovered_cashier_shift(
      v_city_token,'lefteria_fc_academy',v_cashier_shift,1190,v_second_request);
    RAISE EXCEPTION 'city_manager_approved_cash_count';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.review_recovered_cashier_shift(
      v_second_super_token,'lefteria_fc',v_cashier_shift,1190,v_second_request);
    RAISE EXCEPTION 'independent_reviewer_crossed_organization';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Second person independently confirms 11.90 against a 12.00 expectation:
  -- financial variance is flagged, not zeroed or written as a payment.
  v_result:=pos_cashier.review_recovered_cashier_shift(
    v_second_super_token,'lefteria_fc_academy',v_cashier_shift,1190,v_second_request);
  IF v_result->>'reviewer_id'<>v_second_super::text
    OR v_result->>'disposition'<>'variance_flagged'
    OR NOT (v_result->>'requires_investigation')::boolean
    OR (v_result->>'terminal_hold')::boolean
    OR (v_result->>'replay')::boolean
    OR (v_result->>'variance_cents')::bigint<>-10
    OR (v_result->>'difference_between_counts_cents')::bigint<>0
    OR (SELECT count(*) FROM pos_cashier.shift_recovery_reviews
        WHERE shift_id=v_cashier_shift AND reviewer_id=v_second_super)<>1
  THEN RAISE EXCEPTION 'two_person_count_variance_evidence_invalid'; END IF;
  v_result:=pos_cashier.review_recovered_cashier_shift(
    v_second_super_token,'lefteria_fc_academy',v_cashier_shift,1190,v_second_request);
  IF NOT (v_result->>'replay')::boolean
    OR (SELECT count(*) FROM pos_cashier.transaction_audit
        WHERE organization_id='lefteria_fc_academy'
          AND request_id=v_second_request AND action='shift_recovery_review')<>1
  THEN RAISE EXCEPTION 'second_count_replay_not_idempotent'; END IF;
  BEGIN
    PERFORM pos_cashier.review_recovered_cashier_shift(
      v_second_super_token,'lefteria_fc_academy',v_cashier_shift,1180,v_second_request);
    RAISE EXCEPTION 'second_count_mutable_on_replay';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  BEGIN
    UPDATE pos_cashier.shift_recovery_reviews SET disposition='balanced_confirmed'
      WHERE shift_id=v_cashier_shift;
    RAISE EXCEPTION 'second_count_evidence_edited';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    DELETE FROM pos_cashier.shift_recovery_reviews WHERE shift_id=v_cashier_shift;
    RAISE EXCEPTION 'second_count_evidence_deleted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Once independently reviewed, normal use can resume without removing
  -- the recorded ten-cent shortage or touching immutable financial rows.
  v_regular_shift:=(pos_cashier.open_admin_cashier_shift(
    v_local_token,'lefteria_fc_academy',v_register_a1,500,gen_random_uuid())
    ->>'shift_id')::uuid;
  v_result:=pos_cashier.close_admin_cashier_shift(
    v_local_token,'lefteria_fc_academy',v_regular_shift,500,gen_random_uuid());
  IF (v_result->>'variance_cents')::bigint<>0
  THEN RAISE EXCEPTION 'reviewed_shift_blocked_regular_cashier'; END IF;

  -- Recovery completes before removal of a formerly stranded assignment.
  v_result:=pos_cashier.set_admin_terminal_operator(
    v_super_token,'lefteria_fc_academy',v_register_a1,v_cashier,false);
  IF (v_result->>'active')::boolean OR
    EXISTS(SELECT 1 FROM pos_cashier.cashier_assignments
      WHERE organization_id='lefteria_fc_academy' AND register_id=v_register_a1
        AND admin_account_id=v_cashier AND is_active)
  THEN RAISE EXCEPTION 'recovered_shift_terminal_revocation_failed'; END IF;
  -- A different, independent physical count puts this exact register on hold.
  -- There is no automatic override for disputed custody: human incident
  -- investigation must precede further operation or assignment.
  UPDATE identity.admin_accounts SET is_active=true WHERE id=v_cashier;
  PERFORM pos_cashier.set_admin_terminal_operator(
    v_super_token,'lefteria_fc_academy',v_register_a1,v_cashier,true);
  v_disputed_shift:=(pos_cashier.open_admin_cashier_shift(
    v_cashier_token,'lefteria_fc_academy',v_register_a1,750,gen_random_uuid())
    ->>'shift_id')::uuid;
  UPDATE identity.admin_accounts SET is_active=false WHERE id=v_cashier;
  PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
    v_super_token,'lefteria_fc_academy',v_disputed_shift,750,gen_random_uuid(),
    'account_suspended');
  v_result:=pos_cashier.review_recovered_cashier_shift(
    v_second_super_token,'lefteria_fc_academy',v_disputed_shift,740,gen_random_uuid());
  IF v_result->>'disposition'<>'count_disputed'
    OR NOT (v_result->>'terminal_hold')::boolean
    OR (v_result->>'difference_between_counts_cents')::bigint<>-10
  THEN RAISE EXCEPTION 'disputed_second_count_not_held'; END IF;
  BEGIN
    PERFORM pos_cashier.open_admin_cashier_shift(
      v_local_token,'lefteria_fc_academy',v_register_a1,250,gen_random_uuid());
    RAISE EXCEPTION 'disputed_cash_count_register_reopened';
  EXCEPTION WHEN SQLSTATE '55000' THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.set_admin_terminal_operator(
      v_super_token,'lefteria_fc_academy',v_register_a1,v_local_manager,true);
    RAISE EXCEPTION 'disputed_cash_count_operator_reactivated';
  EXCEPTION WHEN SQLSTATE '55000' THEN NULL; END;
  IF EXISTS(SELECT 1 FROM pos_cashier.sales
      WHERE organization_id='lefteria_fc_academy')
    OR EXISTS(SELECT 1 FROM pos_cashier.payment_events
      WHERE organization_id='lefteria_fc_academy')
  THEN RAISE EXCEPTION 'second_count_fabricated_ledger_entries'; END IF;


  v_result:=pos_cashier.read_admin_pos_sites(v_local_token,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'sites')<>1
    OR (v_result->'sites'->0->>'pos_location_id')<>v_pos_a1
  THEN RAISE EXCEPTION 'location_manager_saw_sibling_or_other_city'; END IF;
  v_result:=pos_cashier.read_admin_pos_sites(v_city_token,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'sites')<>2
    OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_result->'sites') AS s
      WHERE s->>'pos_location_id'=v_pos_a1)
    OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_result->'sites') AS s
      WHERE s->>'pos_location_id'=v_pos_a2)
  THEN RAISE EXCEPTION 'city_manager_read_outside_current_city'; END IF;
  v_result:=pos_cashier.read_admin_pos_sites(v_neighbor_token,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'sites')<>1
    OR (v_result->'sites'->0->>'pos_location_id')<>v_pos_b1
  THEN RAISE EXCEPTION 'neighboring_city_manager_saw_other_site'; END IF;
  v_result:=pos_cashier.read_admin_pos_sites(v_unscoped_token,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'sites')<>0
  THEN RAISE EXCEPTION 'unassigned_manager_saw_site_directory'; END IF;
  v_result:=pos_cashier.read_admin_pos_sites(v_super_token,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'sites')<>3
  THEN RAISE EXCEPTION 'super_admin_missing_organization_sites'; END IF;

  -- The only financial result a site manager may access is their mapped site.
  v_result:=pos_cashier.read_admin_pos_site_finances(
    v_local_token,'lefteria_fc_academy',v_pos_a1);
  IF v_result->>'pos_location_id'<>v_pos_a1
    OR (v_result->>'registers_total')::integer<>1
    OR (v_result->>'sales_total')::integer<>0
    OR (v_result->>'sales_paid')::integer<>0
    OR (v_result->>'receipts_issued')::integer<>0
    OR (v_result->>'verified_card_cents')::bigint<>0
    OR (v_result->>'verified_cash_cents')::bigint<>0
    OR (v_result->>'paid_without_receipt')::integer<>0
    OR (v_result->>'finance_posting_active')::boolean
  THEN RAISE EXCEPTION 'site_finance_invented_sales_or_unscoped_totals'; END IF;
  v_result:=pos_cashier.read_admin_pos_site_sales(
    v_local_token,'lefteria_fc_academy',v_pos_a1);
  IF v_result->>'pos_location_id'<>v_pos_a1
    OR (v_result->>'limit')::integer<>50
    OR jsonb_array_length(v_result->'sales')<>0
  THEN RAISE EXCEPTION 'site_sales_history_invented_transactions'; END IF;

  -- No side channel through a different location or the legacy global view.
  BEGIN
    PERFORM pos_cashier.read_admin_pos_site_finances(
      v_local_token,'lefteria_fc_academy',v_pos_a2);
    RAISE EXCEPTION 'location_manager_read_sibling_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_site_finances(
      v_local_token,'lefteria_fc_academy',v_pos_b1);
    RAISE EXCEPTION 'location_manager_read_other_city_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_site_sales(
      v_local_token,'lefteria_fc_academy',v_pos_a2);
    RAISE EXCEPTION 'location_manager_read_sibling_order_history';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_site_finances(
      v_city_token,'lefteria_fc_academy',v_pos_b1);
    RAISE EXCEPTION 'city_manager_read_other_city_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_finance_handoff(
      v_local_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'location_manager_read_global_pos_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_terminal_setup(
      v_local_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'location_manager_read_all_operator_accounts';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos.read_admin_locations(
      v_local_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'location_manager_read_legacy_global_pos_catalogue';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_site_finances(
      v_team_token,'lefteria_fc_academy',v_pos_a1);
    RAISE EXCEPTION 'first_team_manager_read_academy_pos_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_site_finances(
      repeat('0',64),'lefteria_fc_academy',v_pos_a1);
    RAISE EXCEPTION 'forged_admin_session_read_pos_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Revocation of a canonical territory grant must immediately remove
  -- delegated POS privileges, even if an old terminal assignment remains.
  DELETE FROM club.football_location_admin_grants
   WHERE admin_account_id=v_local_manager AND location_id=v_site_a1;
  v_result:=pos_cashier.read_admin_pos_sites(v_local_token,'lefteria_fc_academy');
  IF jsonb_array_length(v_result->'sites')<>0
  THEN RAISE EXCEPTION 'revoked_manager_still_listed_pos_site'; END IF;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_site_finances(
      v_local_token,'lefteria_fc_academy',v_pos_a1);
    RAISE EXCEPTION 'revoked_manager_still_read_pos_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM pos_cashier.current_cashier_authority(
      v_local_token,'lefteria_fc_academy',v_register_a1);
    RAISE EXCEPTION 'revoked_manager_still_owns_assigned_cashier_register';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
   WHERE token_digest=v_city_token;
  BEGIN
    PERFORM pos_cashier.read_admin_pos_sites(v_city_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'revoked_admin_session_still_read_pos_site_directory';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $site_scope$;
ROLLBACK;
