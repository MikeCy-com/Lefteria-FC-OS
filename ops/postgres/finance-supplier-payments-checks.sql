\set ON_ERROR_STOP on
\set ECHO none
-- Independent, disposable finance fixture. No provider calls or live data.
BEGIN;
DO $payment_checks$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_second uuid:=gen_random_uuid();
  v_accountant uuid:=gen_random_uuid();
  v_first uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_city_a uuid:=gen_random_uuid(); v_city_b uuid:=gen_random_uuid();
  v_site_a uuid:=gen_random_uuid(); v_site_b uuid:=gen_random_uuid();
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_second_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_acc_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_first_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_bill uuid; v_bill_b uuid; v_first_bill uuid;
  v_req uuid:=gen_random_uuid(); v_id uuid;
  v_reject uuid:=gen_random_uuid(); v_final uuid:=gen_random_uuid();
  v_self uuid:=gen_random_uuid(); v_first_request uuid:=gen_random_uuid();
  v_read jsonb; v_result jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled) VALUES
    (v_super,'ci-pay-super-'||substr(md5(v_super::text),1,8),
      'CI Primary Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_second,'ci-pay-second-'||substr(md5(v_second::text),1,8),
      'CI Second Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_accountant,'ci-pay-accountant-'||substr(md5(v_accountant::text),1,8),
      'CI Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_first,'ci-pay-first-'||substr(md5(v_first::text),1,8),
      'CI First Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-pay-manager-'||substr(md5(v_manager::text),1,8),
      'CI Site Manager','manager',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(
    admin_account_id,organization_id) VALUES
    (v_super,'lefteria_fc_academy'),(v_super,'lefteria_fc'),
    (v_second,'lefteria_fc_academy'),
    (v_accountant,'lefteria_fc_academy'),
    (v_first,'lefteria_fc'),
    (v_manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES
    (gen_random_uuid(),v_super_token,v_super,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_second_token,v_second,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_acc_token,v_accountant,repeat('c',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_first_token,v_first,repeat('d',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('e',64),clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO club.football_cities(id,name,slug) VALUES
    (v_city_a,'CI Payment City A','payment-city-a-'||substr(md5(v_city_a::text),1,8)),
    (v_city_b,'CI Payment City B','payment-city-b-'||substr(md5(v_city_b::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug) VALUES
    (v_site_a,v_city_a,'CI Payment North','north'),
    (v_site_b,v_city_b,'CI Payment South','south');
  INSERT INTO club.football_operational_scopes(
    id,organization_id,academy_city_id)
  VALUES (gen_random_uuid(),'lefteria_fc_academy',v_city_a),
    (gen_random_uuid(),'lefteria_fc_academy',v_city_b);
  INSERT INTO club.football_location_admin_grants(
    admin_account_id,location_id,role)
  VALUES (v_manager,v_site_a,'location_manager');

  v_bill:=(finance.create_admin_supplier_bill(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Equipment Vendor','PAY-N-'||substr(md5(v_req::text),1,8),
    'equipment','Academy equipment',v_month,13500,v_site_a)->>'id')::uuid;
  v_bill_b:=(finance.create_admin_supplier_bill(
    v_super_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Facility Vendor','PAY-S-'||substr(md5(v_req::text),1,8),
    'facilities','Academy rental',v_month,24000,v_site_b)->>'id')::uuid;
  v_first_bill:=(finance.create_admin_supplier_bill(
    v_first_token,'lefteria_fc',gen_random_uuid(),
    'CI First Vendor','PAY-F-'||substr(md5(v_req::text),1,8),
    'travel','First Team bus',v_month,48000,NULL)->>'id')::uuid;

  v_result:=finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',v_req,v_bill,5000,v_month,
    'bank_transfer','CI Bank evidence A');
  v_id:=(v_result->>'id')::uuid;
  IF v_result->>'status'<>'pending'
    OR v_result->>'bank_verified'<>'false'
    OR v_result->>'posted_to_general_ledger'<>'false'
  THEN RAISE EXCEPTION 'unreviewed_payment_claimed_paid'; END IF;
  v_read:=finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',v_req,v_bill,5000,v_month,
    'bank_transfer','CI Bank evidence A');
  IF v_read->>'replay'<>'true' OR (v_read->>'id')::uuid<>v_id
  THEN RAISE EXCEPTION 'payment_request_replay_invalid'; END IF;
  BEGIN
    PERFORM finance.create_admin_supplier_payment_request(
      v_acc_token,'lefteria_fc_academy',v_req,v_bill,6000,v_month,
      'bank_transfer','CI Bank evidence A');
    RAISE EXCEPTION 'conflicting_payment_request_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  v_read:=finance.read_admin_supplier_payment_register(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_read#>>'{summary,bill_count}')::int<>2
    OR (v_read#>>'{summary,obligations_cents}')::bigint<>37500
    OR (v_read#>>'{summary,approved_manual_cents}')::bigint<>0
    OR (v_read#>>'{summary,remaining_cents}')::bigint<>37500
    OR (v_read#>>'{summary,pending_evidence_cents}')::bigint<>5000
    OR v_read::text LIKE '%CI First Vendor%'
  THEN RAISE EXCEPTION 'pending_payment_must_not_count_as_paid'; END IF;
  BEGIN
    PERFORM finance.review_admin_supplier_payment_request(
      v_acc_token,'lefteria_fc_academy',v_id,true,
      'Independently verified transfer');
    RAISE EXCEPTION 'accountant_approved_own_evidence';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_payment_register(
      v_manager_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'site_manager_leaked_supplier_payment_evidence';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_payment_request(
      v_first_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,1000,v_month,
      'cash','Wrong owner voucher');
    RAISE EXCEPTION 'first_team_accountant_accessed_academy';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  v_result:=finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_id,true,
    'Statement independently reviewed');
  IF v_result->>'status'<>'approved_manual'
    OR v_result->>'bank_verified'<>'false'
    OR v_result->>'posted_to_general_ledger'<>'false'
  THEN RAISE EXCEPTION 'manual_review_claimed_bank_or_gl_posting'; END IF;
  v_result:=finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_id,true,
    'Statement independently reviewed');
  IF v_result->>'replay'<>'true'
  THEN RAISE EXCEPTION 'review_replay_invalid'; END IF;
  BEGIN
    PERFORM finance.review_admin_supplier_payment_request(
      v_super_token,'lefteria_fc_academy',v_id,false,
      'Changed previous decision');
    RAISE EXCEPTION 'review_decision_mutable';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  v_reject:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,9000,v_month,
    'external_card','CI external payment B')->>'id')::uuid;
  BEGIN
    PERFORM finance.review_admin_supplier_payment_request(
      v_super_token,'lefteria_fc_academy',v_reject,true,
      'Reviewed second transfer');
    RAISE EXCEPTION 'supplier_bill_overpayment_accepted';
  EXCEPTION WHEN check_violation THEN NULL; END;

  v_final:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,8500,v_month,
    'cash','CI cash voucher C')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_final,true,
    'Cash voucher independently reviewed');
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_reject,false,
    'No matching external settlement');
  v_read:=finance.read_admin_supplier_payment_register(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_read#>>'{summary,approved_manual_cents}')::bigint<>13500
    OR (v_read#>>'{summary,remaining_cents}')::bigint<>24000
    OR (v_read#>>'{summary,pending_evidence_cents}')::bigint<>0
    OR jsonb_array_length(v_read->'requests')<>3
  THEN RAISE EXCEPTION 'approved_rejected_partial_totals_wrong'; END IF;

  v_self:=(finance.create_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',gen_random_uuid(),v_bill_b,2000,v_month,
    'bank_transfer','CI facility transfer')->>'id')::uuid;
  BEGIN
    PERFORM finance.review_admin_supplier_payment_request(
      v_super_token,'lefteria_fc_academy',v_self,true,
      'Own voucher approved wrong');
    RAISE EXCEPTION 'super_admin_approved_own_request';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  PERFORM finance.review_admin_supplier_payment_request(
    v_second_token,'lefteria_fc_academy',v_self,true,
    'Another Super Admin reviewed');
  v_read:=finance.read_admin_supplier_payment_register(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_read#>>'{summary,approved_manual_cents}')::bigint<>15500
    OR (v_read#>>'{summary,remaining_cents}')::bigint<>22000
  THEN RAISE EXCEPTION 'four_eyes_partial_allocation_invalid'; END IF;

  v_first_request:=(finance.create_admin_supplier_payment_request(
    v_first_token,'lefteria_fc',gen_random_uuid(),v_first_bill,3000,v_month,
    'bank_transfer','CI First Team statement')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc',v_first_request,true,
    'First Team transfer checked');
  v_read:=finance.read_admin_supplier_payment_register(
    v_first_token,'lefteria_fc',v_month);
  IF (v_read#>>'{summary,obligations_cents}')::bigint<>48000
    OR (v_read#>>'{summary,approved_manual_cents}')::bigint<>3000
    OR v_read::text LIKE '%CI Equipment Vendor%'
  THEN RAISE EXCEPTION 'first_team_supplier_payment_leak'; END IF;

  BEGIN
    UPDATE finance.supplier_payment_requests SET amount_cents=1 WHERE id=v_id;
    RAISE EXCEPTION 'supplier_payment_evidence_updated';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    DELETE FROM finance.supplier_payment_decisions WHERE request_id=v_id;
    RAISE EXCEPTION 'supplier_payment_approval_deleted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_acc_token;
  BEGIN
    PERFORM finance.read_admin_supplier_payment_register(
      v_acc_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'revoked_accountant_read_payment_evidence';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF (SELECT count(*) FROM audit.security_events
    WHERE actor_identity_id=v_super AND organization_id='lefteria_fc_academy'
      AND action='admin.finance.supplier_payment.review')<3
    OR NOT EXISTS(SELECT 1 FROM audit.security_events
       WHERE actor_identity_id=v_accountant AND
         action='admin.finance.supplier_payment.request')
  THEN RAISE EXCEPTION 'supplier_payment_audit_events_missing'; END IF;
END $payment_checks$;
ROLLBACK;
