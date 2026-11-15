\set ON_ERROR_STOP on
\set ECHO none
-- Disposable fixture only: no provider interaction, all synthetic rows rolled back.
BEGIN;
DO $reversal_checks$
DECLARE
  v_super uuid:=gen_random_uuid(); v_reviewer uuid:=gen_random_uuid();
  v_accountant uuid:=gen_random_uuid(); v_first uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_city uuid:=gen_random_uuid(); v_site uuid:=gen_random_uuid();
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_reviewer_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_acc_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_first_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_bill uuid; v_first_bill uuid; v_payment uuid; v_credit uuid;
  v_new_payment uuid; v_last_payment uuid; v_rejected_payment uuid;
  v_pay_reversal uuid; v_credit_reversal uuid; v_rejected_reversal uuid;
  v_client uuid:=gen_random_uuid(); v_report jsonb; v_result jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled)
  VALUES
    (v_super,'ci-reverse-super-'||substr(md5(v_super::text),1,8),
      'CI Original Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_reviewer,'ci-reverse-reviewer-'||substr(md5(v_reviewer::text),1,8),
      'CI Independent Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_accountant,'ci-reverse-accountant-'||substr(md5(v_accountant::text),1,8),
      'CI Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_first,'ci-reverse-first-'||substr(md5(v_first::text),1,8),
      'CI First Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-reverse-manager-'||substr(md5(v_manager::text),1,8),
      'CI Location Manager','manager',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES
    (v_super,'lefteria_fc_academy'),(v_super,'lefteria_fc'),
    (v_reviewer,'lefteria_fc_academy'),(v_reviewer,'lefteria_fc'),
    (v_accountant,'lefteria_fc_academy'),
    (v_first,'lefteria_fc'),
    (v_manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES
    (gen_random_uuid(),v_super_token,v_super,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_reviewer_token,v_reviewer,repeat('b',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_acc_token,v_accountant,repeat('c',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_first_token,v_first,repeat('d',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('e',64),
      clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO club.football_cities(id,name,slug) VALUES
    (v_city,'CI Reversal City','reversal-city-'||substr(md5(v_city::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug)
    VALUES(v_site,v_city,'CI Reversal Site','reversal-site');
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
    VALUES(gen_random_uuid(),'lefteria_fc_academy',v_city);
  INSERT INTO club.football_location_admin_grants(
    admin_account_id,location_id,role)
    VALUES(v_manager,v_site,'location_manager');

  v_bill:=(finance.create_admin_supplier_bill(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Private Reversal Vendor','CI-REV-'||substr(md5(v_city::text),1,8),
    'equipment','Academy balls and equipment',v_month,20000,v_site)->>'id')::uuid;
  v_first_bill:=(finance.create_admin_supplier_bill(
    v_first_token,'lefteria_fc',gen_random_uuid(),
    'CI First Team Vendor','CI-FT-REV-'||substr(md5(v_city::text),1,8),
    'travel','First Team transport',v_month,16000,NULL)->>'id')::uuid;
  v_payment:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    6000,v_month,'bank_transfer','CI original proof')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_payment,true,
    'Original evidence reviewed offsite');
  v_credit:=(finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    'CI-CREDIT-REV',v_month,4000,
    'Supplier equipment credit note')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',v_credit,true,
    'Original supplier credit approved');

  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_manual_cents}')::bigint<>6000
    OR (v_report#>>'{summary,approved_credit_cents}')::bigint<>4000
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>10000
  THEN RAISE EXCEPTION 'reversal_baseline_wrong'; END IF;

  v_result:=finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',v_client,'payment',v_payment,
    'Duplicate bank evidence was incorrectly approved');
  v_pay_reversal:=(v_result->>'id')::uuid;
  IF v_result->>'status'<>'pending'
    OR v_result->>'bank_verified'<>'false'
    OR v_result->>'bank_refund_recorded'<>'false'
    OR v_result->>'posted_to_general_ledger'<>'false'
  THEN RAISE EXCEPTION 'reversal_request_falsely_reported_paid'; END IF;
  v_result:=finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',v_client,'payment',v_payment,
    'Duplicate bank evidence was incorrectly approved');
  IF v_result->>'replay'<>'true'
    OR (v_result->>'id')::uuid<>v_pay_reversal
  THEN RAISE EXCEPTION 'reversal_request_replay_invalid'; END IF;
  BEGIN
    PERFORM finance.create_admin_supplier_approval_reversal(
      v_acc_token,'lefteria_fc_academy',v_client,'payment',v_payment,
      'Different reason attempting same idempotency key');
    RAISE EXCEPTION 'reversal_idempotency_payload_changed';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_manual_cents}')::bigint<>6000
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>10000
    OR jsonb_array_length(v_report->'reversals')<>1
    OR v_report->'reversals'->0->>'status'<>'pending'
  THEN RAISE EXCEPTION 'pending_reversal_changed_liability'; END IF;

  BEGIN
    PERFORM finance.review_admin_supplier_approval_reversal(
      v_acc_token,'lefteria_fc_academy',v_pay_reversal,true,
      'Accountant attempted to approve own correction');
    RAISE EXCEPTION 'accountant_approved_reversal';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.review_admin_supplier_approval_reversal(
      v_super_token,'lefteria_fc_academy',v_pay_reversal,true,
      'Original approval reviewer cannot clear own error');
    RAISE EXCEPTION 'original_reviewer_approved_own_reversal';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_approval_reversal(
      v_manager_token,'lefteria_fc_academy',gen_random_uuid(),
      'payment',v_payment,'Location manager should not read vendor');
    RAISE EXCEPTION 'location_manager_created_supplier_reversal';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_payable_position(
      v_manager_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'location_manager_read_supplier_reversals';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_approval_reversal(
      v_first_token,'lefteria_fc_academy',gen_random_uuid(),
      'payment',v_payment,'Wrong organization attempted reversal');
    RAISE EXCEPTION 'first_team_accountant_reversed_academy_record';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_approval_reversal(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
      'payment',v_first_bill,'Wrong target from another organization');
    RAISE EXCEPTION 'foreign_bill_accepted_as_approved_payment';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;

  v_result:=finance.review_admin_supplier_approval_reversal(
    v_reviewer_token,'lefteria_fc_academy',v_pay_reversal,true,
    'Independent evidence correction is documented');
  IF v_result->>'status'<>'approved_manual'
    OR v_result->>'bank_verified'<>'false'
  THEN RAISE EXCEPTION 'payment_reversal_review_not_verified'; END IF;
  v_result:=finance.review_admin_supplier_approval_reversal(
    v_reviewer_token,'lefteria_fc_academy',v_pay_reversal,true,
    'Independent evidence correction is documented');
  IF v_result->>'replay'<>'true'
  THEN RAISE EXCEPTION 'payment_reversal_review_replay_invalid'; END IF;
  BEGIN
    PERFORM finance.review_admin_supplier_approval_reversal(
      v_reviewer_token,'lefteria_fc_academy',v_pay_reversal,false,
      'Changed a final review decision after approval');
    RAISE EXCEPTION 'supplier_reversal_decision_changed';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_manual_cents}')::bigint<>0
    OR (v_report#>>'{summary,approved_credit_cents}')::bigint<>4000
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>16000
    OR v_report->'requests'->0->>'reversed_manual'<>'true'
    OR (v_report->'bills'->0->>'approved_manual_cents')::bigint<>0
    OR v_report->'reversals'->0->>'status'<>'approved_manual'
  THEN RAISE EXCEPTION 'approved_payment_reversal_not_reflected'; END IF;
  BEGIN
    PERFORM finance.create_admin_supplier_approval_reversal(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
      'payment',v_payment,'Duplicate already completed correction attempt');
    RAISE EXCEPTION 'duplicate_approved_payment_reversal_created';
  EXCEPTION WHEN check_violation THEN NULL; END;
  v_result:=finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',v_client,'payment',v_payment,
    'Duplicate bank evidence was incorrectly approved');
  IF v_result->>'status'<>'approved_manual' OR v_result->>'replay'<>'true'
  THEN RAISE EXCEPTION 'approved_reversal_replay_stale'; END IF;

  -- The reversed payment is no longer used to consume credit-adjusted
  -- headroom. A new independent approval can use the recovered balance.
  v_new_payment:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    16000,v_month,'cash','CI replacement voucher')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_new_payment,true,
    'Replacement manual evidence independently checked');
  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_manual_cents}')::bigint<>16000
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>0
  THEN RAISE EXCEPTION 'replacement_payment_after_reversal_failed'; END IF;

  v_rejected_reversal:=(finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'payment',v_new_payment,'Evidence later confirmed to be authentic')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_approval_reversal(
    v_reviewer_token,'lefteria_fc_academy',v_rejected_reversal,false,
    'Independent check rejected invalid correction claim');
  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,remaining_cents}')::bigint<>0
  THEN RAISE EXCEPTION 'rejected_reversal_changed_liability'; END IF;

  v_result:=finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),'credit',v_credit,
    'Supplier credit note was recorded in error');
  v_credit_reversal:=(v_result->>'id')::uuid;
  BEGIN
    PERFORM finance.review_admin_supplier_approval_reversal(
      v_super_token,'lefteria_fc_academy',v_credit_reversal,true,
      'Original credit reviewer attempting own reversal');
    RAISE EXCEPTION 'credit_original_reviewer_cannot_reverse';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  PERFORM finance.review_admin_supplier_approval_reversal(
    v_reviewer_token,'lefteria_fc_academy',v_credit_reversal,true,
    'Independent supplier credit reversal examined');
  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_manual_cents}')::bigint<>16000
    OR (v_report#>>'{summary,approved_credit_cents}')::bigint<>0
    OR (v_report#>>'{summary,net_payables_cents}')::bigint<>20000
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>4000
    OR v_report->'credit_notes'->0->>'reversed_manual'<>'true'
    OR jsonb_array_length(v_report->'reversals')<>3
  THEN RAISE EXCEPTION 'credit_reversal_not_reflected'; END IF;
  BEGIN
    PERFORM finance.create_admin_supplier_approval_reversal(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
      'credit',v_credit,'Duplicate supplier credit reversal request');
    RAISE EXCEPTION 'duplicate_credit_reversal_created';
  EXCEPTION WHEN check_violation THEN NULL; END;

  -- The original credit has been reversed: the new payment can reach the
  -- original invoice's full gross without violating combined approval caps.
  v_last_payment:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    4000,v_month,'bank_transfer','CI final voucher')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_last_payment,true,
    'Final balance manually reviewed after credit reversal');
  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_manual_cents}')::bigint<>20000
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>0
    OR v_report::text NOT LIKE '%reversed_manual%'
    OR v_report::text LIKE '%CI First Team Vendor%'
  THEN RAISE EXCEPTION 'reversal_effective_liability_not_rebalanced'; END IF;

  v_rejected_payment:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    1000,v_month,'cash','CI rejected proof')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_rejected_payment,false,
    'Evidence rejected before approval');
  BEGIN
    PERFORM finance.create_admin_supplier_approval_reversal(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
      'payment',v_rejected_payment,'Cannot reverse an already rejected voucher');
    RAISE EXCEPTION 'rejected_original_accepted_for_reversal';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;

  BEGIN
    UPDATE finance.supplier_review_reversal_requests
    SET reason='changed original reason' WHERE id=v_pay_reversal;
    RAISE EXCEPTION 'reversal_request_was_mutable';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    DELETE FROM finance.supplier_review_reversal_decisions
    WHERE request_id=v_pay_reversal;
    RAISE EXCEPTION 'reversal_decision_was_mutable';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_acc_token;
  BEGIN
    PERFORM finance.read_admin_supplier_payable_position(
      v_acc_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'revoked_accountant_read_reversal_register';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF (SELECT count(*) FROM audit.security_events
    WHERE actor_identity_id=v_accountant
      AND action='admin.finance.supplier_reversal.request')<2
    OR (SELECT count(*) FROM audit.security_events
      WHERE actor_identity_id=v_reviewer
        AND action='admin.finance.supplier_reversal.review')<2
  THEN RAISE EXCEPTION 'reversal_audit_events_missing'; END IF;
END $reversal_checks$;
ROLLBACK;
