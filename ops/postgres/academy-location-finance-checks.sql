\set ON_ERROR_STOP on
\set ECHO none
-- Disposable PostgreSQL acceptance. No external provider, no real families.
BEGIN;
DO $test$
DECLARE
  v_admin uuid:=gen_random_uuid(); v_manager uuid:=gen_random_uuid();
  v_local uuid:=gen_random_uuid(); v_outsider uuid:=gen_random_uuid();
  v_parent uuid:=gen_random_uuid();
  v_city_a uuid:=gen_random_uuid();v_city_b uuid:=gen_random_uuid();
  v_site_a uuid:=gen_random_uuid();v_site_b uuid:=gen_random_uuid();
  v_scope_a uuid:=gen_random_uuid();v_scope_b uuid:=gen_random_uuid();
  v_group_a text:='ci-g-'||md5(gen_random_uuid()::text);
  v_group_b text:='ci-g-'||md5(gen_random_uuid()::text);
  v_group_legacy text:='ci-g-'||md5(gen_random_uuid()::text);
  v_p_a text:='ci-p-'||md5(gen_random_uuid()::text);
  v_p_b text:='ci-p-'||md5(gen_random_uuid()::text);
  v_p_legacy text:='ci-p-'||md5(gen_random_uuid()::text);
  v_fee_a uuid:=gen_random_uuid();v_fee_b uuid:=gen_random_uuid();
  v_fee_legacy uuid:=gen_random_uuid();v_fee_advance uuid:=gen_random_uuid();
  v_general uuid:=gen_random_uuid();v_event uuid:=gen_random_uuid();
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_local_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_outsider_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_data jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled) VALUES
    (v_admin,'ci-finance-super-'||substr(md5(v_admin::text),1,8),'CI Finance Owner',
       'super_admin',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-finance-manager-'||substr(md5(v_manager::text),1,8),'CI Finance City',
       'manager',true,'ci-fixture-hash',false,false),
    (v_local,'ci-finance-local-'||substr(md5(v_local::text),1,8),'CI Finance Site',
       'manager',true,'ci-fixture-hash',false,false),
    (v_outsider,'ci-finance-other-'||substr(md5(v_outsider::text),1,8),'CI Finance Outside',
       'manager',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES (v_admin,'lefteria_fc_academy'),(v_admin,'lefteria_fc'),
   (v_manager,'lefteria_fc_academy'),(v_local,'lefteria_fc_academy'),
   (v_outsider,'lefteria_fc');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,
    credential_digest,created_at,expires_at) VALUES
    (gen_random_uuid(),v_super_token,v_admin,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_local_token,v_local,repeat('c',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_outsider_token,v_outsider,repeat('d',64),clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO identity.mobile_identities(
    id,normalized_phone,role,display_name,is_active,platform,created_at)
    VALUES(v_parent,'35799'||substr(replace(v_parent::text,'-',''),1,6),
      'parent','CI Finance Parent',true,'mobile',clock_timestamp());

  INSERT INTO club.football_cities(id,name,slug) VALUES
    (v_city_a,'CI Finance City A','finance-city-a-'||substr(md5(v_city_a::text),1,7)),
    (v_city_b,'CI Finance City B','finance-city-b-'||substr(md5(v_city_b::text),1,7));
  INSERT INTO club.football_locations(id,city_id,name,slug) VALUES
    (v_site_a,v_city_a,'CI North','north'),
    (v_site_b,v_city_b,'CI South','south');
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
  VALUES (v_scope_a,'lefteria_fc_academy',v_city_a),
    (v_scope_b,'lefteria_fc_academy',v_city_b);
  INSERT INTO club.football_city_admin_grants(admin_account_id,city_id,role)
  VALUES (v_manager,v_city_a,'city_manager');
  INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role)
  VALUES (v_local,v_site_a,'location_manager');
  INSERT INTO club.academy_groups(id,organization_id,name)
  VALUES (v_group_a,'lefteria_fc_academy','CI Finance Academy U10'),
    (v_group_b,'lefteria_fc_academy','CI Finance Academy U12'),
    (v_group_legacy,'lefteria_fc_academy','CI Finance Legacy U14');
  INSERT INTO club.academy_group_location_assignments(
    organization_id,group_id,city_id,location_id,scope_id)
  VALUES ('lefteria_fc_academy',v_group_a,v_city_a,v_site_a,v_scope_a),
    ('lefteria_fc_academy',v_group_b,v_city_b,v_site_b,v_scope_b);
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
  VALUES (v_p_a,'lefteria_fc_academy','CI Finance North',10,'Forward','Academy',true,false,false,true),
    (v_p_b,'lefteria_fc_academy','CI Finance South',11,'Forward','Academy',true,false,false,true),
    (v_p_legacy,'lefteria_fc_academy','CI Finance Legacy',12,'Forward','Academy',true,false,false,true);
  INSERT INTO club.player_academy_groups(organization_id,player_id,academy_group_id)
  VALUES ('lefteria_fc_academy',v_p_a,v_group_a),
    ('lefteria_fc_academy',v_p_b,v_group_b),
    ('lefteria_fc_academy',v_p_legacy,v_group_legacy);

  INSERT INTO finance.training_fee_issues(id,organization_id,player_id,
    payer_identity_id,fee_month,amount_cents,payer_phone,payer_email,
    payer_first_name,payer_last_name,issued_by)
  VALUES
    (v_fee_a,'lefteria_fc_academy',v_p_a,v_parent,v_month,4800,
      '+35799123456','test@example.invalid','Test','Parent',v_admin),
    (v_fee_b,'lefteria_fc_academy',v_p_b,v_parent,v_month,5200,
      '+35799123456','test@example.invalid','Test','Parent',v_admin),
    (v_fee_legacy,'lefteria_fc_academy',v_p_legacy,v_parent,v_month,4200,
      '+35799123456','test@example.invalid','Test','Parent',v_admin),
    (v_fee_advance,'lefteria_fc_academy',v_p_a,v_parent,(v_month+interval '1 month')::date,4800,
      '+35799123456','test@example.invalid','Test','Parent',v_admin);
  IF (SELECT location_id FROM finance.academy_training_fee_locations
      WHERE issue_id=v_fee_a) IS DISTINCT FROM v_site_a
    OR (SELECT location_id FROM finance.academy_training_fee_locations
      WHERE issue_id=v_fee_b) IS DISTINCT FROM v_site_b
    OR EXISTS(SELECT 1 FROM finance.academy_training_fee_locations
      WHERE issue_id IN (v_fee_legacy,v_fee_advance))
  THEN RAISE EXCEPTION 'issue_time_site_snapshots_incorrect'; END IF;

  -- Current roster changes cannot change issue-time snapshot.
  DELETE FROM club.player_academy_groups WHERE player_id=v_p_a;
  INSERT INTO club.player_academy_groups(organization_id,player_id,academy_group_id)
    VALUES('lefteria_fc_academy',v_p_a,v_group_b);
  IF (SELECT location_id FROM finance.academy_training_fee_locations
    WHERE issue_id=v_fee_a)<>v_site_a THEN
    RAISE EXCEPTION 'location_snapshot_changed_with_roster'; END IF;
  BEGIN
    UPDATE finance.academy_training_fee_locations SET location_id=v_site_b
      WHERE issue_id=v_fee_a;
    RAISE EXCEPTION 'location_snapshot_was_mutable';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  INSERT INTO finance.general_charge_issues(
    id,organization_id,player_id,charge_type,description,amount_cents,
    issued_at,created_by
  ) VALUES(v_general,'lefteria_fc_academy',v_p_a,'equipment',
    'CI site supplies',8000,clock_timestamp(),v_admin);
  INSERT INTO finance.general_charge_events(
    id,charge_id,organization_id,idempotency_id,event_kind,
    payment_method,evidence_type,amount_cents,actor_id)
  VALUES(v_event,v_general,'lefteria_fc_academy',gen_random_uuid(),
    'payment','cash','admin_cash',2000,v_admin);

  v_data:=finance.read_admin_academy_location_finance(
    v_local_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_data->'locations')<>1
    OR (v_data->'locations'->0->>'location_id')::uuid<>v_site_a
    OR (v_data->'locations'->0->>'training_issued_cents')::bigint<>4800
    OR (v_data->'locations'->0->>'general_issued_count')::int<>0
  THEN RAISE EXCEPTION 'location_manager_finance_leak'; END IF;
  v_data:=finance.read_admin_academy_location_finance(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_data->'locations')<>1
  THEN RAISE EXCEPTION 'city_manager_other_city_finance_leak'; END IF;
  v_data:=finance.read_admin_academy_location_finance(
    v_super_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_data->'locations')<>2
  THEN RAISE EXCEPTION 'superadmin_multi_site_finance_missing'; END IF;
  BEGIN
    PERFORM finance.read_admin_academy_unallocated_issues(
      v_local_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'location_manager_leaked_unallocated_invoices';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_academy_location_finance(
      v_outsider_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'first_team_finance_accessed_academy';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  v_data:=finance.read_admin_academy_unallocated_issues(
    v_super_token,'lefteria_fc_academy',v_month);
  IF (v_data->>'total_count')::int<>2
    OR jsonb_array_length(v_data->'issues')<>2
  THEN RAISE EXCEPTION 'unallocated_backlog_mismatch'; END IF;

  BEGIN
    PERFORM finance.assign_admin_academy_issue_site(
      v_local_token,'lefteria_fc_academy','general_charge',
      v_general,v_site_a,'verified CI reference');
    RAISE EXCEPTION 'location_manager_allocated_finance';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.assign_admin_academy_issue_site(
      v_super_token,'lefteria_fc_academy','training_fee',
      v_fee_a,v_site_b,'duplicate CI reference');
    RAISE EXCEPTION 'already_issued_snapshot_reallocated';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  v_data:=finance.assign_admin_academy_issue_site(
    v_super_token,'lefteria_fc_academy','general_charge',
    v_general,v_site_a,'CI-verified-invoice-source-2026');
  IF v_data->>'method'<>'manually_verified'
    OR (SELECT location_id FROM finance.academy_general_charge_locations
      WHERE issue_id=v_general)<>v_site_a
  THEN RAISE EXCEPTION 'verified_general_charge_not_attributed'; END IF;
  v_data:=finance.read_admin_academy_location_finance(
    v_local_token,'lefteria_fc_academy',v_month);
  IF (v_data->'locations'->0->>'general_issued_cents')::bigint<>8000
    OR (v_data->'locations'->0->>'general_paid_cents')::bigint<>2000
  THEN RAISE EXCEPTION 'site_finance_immutable_ledger_totals_mismatch'; END IF;
  v_data:=finance.read_admin_academy_unallocated_issues(
    v_super_token,'lefteria_fc_academy',v_month);
  IF (v_data->>'total_count')::int<>1 THEN
    RAISE EXCEPTION 'manual_reconciliation_failed_to_remove_backlog'; END IF;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_local_token;
  BEGIN
    PERFORM finance.read_admin_academy_location_finance(
      v_local_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'revoked_session_leaked_site_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $test$;
ROLLBACK;
