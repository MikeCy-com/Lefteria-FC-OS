\set ON_ERROR_STOP on
\set ECHO none
-- Synthetic acceptance for Academy POS site/terminal/finance authority.
-- Run ONLY against disposable migrated PostgreSQL. No sale, payment, refund,
-- receipt, stock posting, Payabl request or real person is created.
BEGIN;
DO $site_scope$
DECLARE
  v_super uuid:=gen_random_uuid();
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
  -- A real unpaid sale blocks emergency closure. It is synthetic and
  -- deleted inside the disposable test transaction before the positive path.
  INSERT INTO pos_cashier.sales(
    id,organization_id,register_id,shift_id,cashier_id,
    request_id,subtotal_cents,total_cents)
  VALUES(v_pending_sale,'lefteria_fc_academy',v_register_a1,
    v_cashier_shift,v_cashier,gen_random_uuid(),100,100);
  BEGIN
    PERFORM pos_cashier.recover_suspended_admin_cashier_shift(
      v_super_token,'lefteria_fc_academy',v_cashier_shift,
      1190,v_recovery_request,'account_suspended');
    RAISE EXCEPTION 'supervisor_recovered_shift_with_pending_sale';
  EXCEPTION WHEN SQLSTATE '55000' THEN NULL; END;
  DELETE FROM pos_cashier.sales
    WHERE organization_id='lefteria_fc_academy' AND id=v_pending_sale;

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

  -- Recovery completes before removal of a formerly stranded assignment.
  v_result:=pos_cashier.set_admin_terminal_operator(
    v_super_token,'lefteria_fc_academy',v_register_a1,v_cashier,false);
  IF (v_result->>'active')::boolean OR
    EXISTS(SELECT 1 FROM pos_cashier.cashier_assignments
      WHERE organization_id='lefteria_fc_academy' AND register_id=v_register_a1
        AND admin_account_id=v_cashier AND is_active)
  THEN RAISE EXCEPTION 'recovered_shift_terminal_revocation_failed'; END IF;

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
