\set ON_ERROR_STOP on
\set ECHO none
-- Synthetic, disposable database test: no production finance/customer data.
BEGIN;
DO $credit_checks$
DECLARE
  v_super uuid:=gen_random_uuid(); v_second uuid:=gen_random_uuid();
  v_acc uuid:=gen_random_uuid(); v_first uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_city uuid:=gen_random_uuid(); v_site uuid:=gen_random_uuid();
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_second_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_acc_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_first_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_bill uuid; v_ft_bill uuid; v_req uuid:=gen_random_uuid();
  v_credit uuid; v_duplicate uuid; v_payment uuid; v_second_payment uuid;
  v_report jsonb; v_result jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled)
  VALUES
    (v_super,'ci-credit-super-'||substr(md5(v_super::text),1,8),
      'CI Credit Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_second,'ci-credit-second-'||substr(md5(v_second::text),1,8),
      'CI Credit Second','super_admin',true,'ci-fixture-hash',false,false),
    (v_acc,'ci-credit-accountant-'||substr(md5(v_acc::text),1,8),
      'CI Credit Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_first,'ci-credit-first-'||substr(md5(v_first::text),1,8),
      'CI Credit First','accountant',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-credit-manager-'||substr(md5(v_manager::text),1,8),
      'CI Credit Manager','manager',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(
    admin_account_id,organization_id) VALUES
    (v_super,'lefteria_fc_academy'),
    (v_super,'lefteria_fc'),
    (v_second,'lefteria_fc_academy'),
    (v_acc,'lefteria_fc_academy'),
    (v_first,'lefteria_fc'),
    (v_manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES
    (gen_random_uuid(),v_super_token,v_super,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_second_token,v_second,repeat('b',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_acc_token,v_acc,repeat('c',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_first_token,v_first,repeat('d',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('e',64),
      clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO club.football_cities(id,name,slug)
    VALUES(v_city,'CI Credit City','credit-city-'||substr(md5(v_city::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug)
    VALUES(v_site,v_city,'CI Credit Site','credit-site');
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
    VALUES(gen_random_uuid(),'lefteria_fc_academy',v_city);
  INSERT INTO club.football_location_admin_grants(admin_account_id,location_id,role)
    VALUES(v_manager,v_site,'location_manager');

  v_bill:=(finance.create_admin_supplier_bill(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Credit Vendor','BILL-'||substr(md5(v_req::text),1,8),
    'equipment','CI original supplier bill',v_month,13500,v_site)->>'id')::uuid;
  v_ft_bill:=(finance.create_admin_supplier_bill(
    v_first_token,'lefteria_fc',gen_random_uuid(),
    'CI First Credit Vendor','BILL-FT-'||substr(md5(v_req::text),1,8),
    'travel','CI First Team invoice',v_month,8000,NULL)->>'id')::uuid;

  v_payment:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    5000,v_month,'bank_transfer','CI payment proof')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_payment,true,
    'Checked with external paperwork');

  v_result:=finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',v_req,v_bill,
    'CI-CREDIT-'||substr(md5(v_req::text),1,8),
    v_month,4500,'Supplier equipment returned');
  v_credit:=(v_result->>'id')::uuid;
  IF v_result->>'status'<>'pending'
    OR v_result->>'bank_refund_recorded'<>'false'
    OR v_result->>'posted_to_general_ledger'<>'false'
  THEN RAISE EXCEPTION 'credit_note_claimed_refund_or_gl'; END IF;
  v_result:=finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',v_req,v_bill,
    'CI-CREDIT-'||substr(md5(v_req::text),1,8),
    v_month,4500,'Supplier equipment returned');
  IF v_result->>'replay'<>'true' OR (v_result->>'id')::uuid<>v_credit
  THEN RAISE EXCEPTION 'supplier_credit_idempotent_replay_invalid'; END IF;

  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,obligations_cents}')::bigint<>13500
    OR (v_report#>>'{summary,approved_manual_cents}')::bigint<>5000
    OR (v_report#>>'{summary,approved_credit_cents}')::bigint<>0
    OR (v_report#>>'{summary,pending_credit_cents}')::bigint<>4500
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>8500
    OR jsonb_array_length(v_report->'credit_notes')<>1
    OR v_report::text LIKE '%CI First Credit Vendor%'
  THEN RAISE EXCEPTION 'pending_credit_wrongly_reduced_balance'; END IF;

  BEGIN
    PERFORM finance.review_admin_supplier_credit_request(
      v_acc_token,'lefteria_fc_academy',v_credit,true,
      'Accountant cannot approve own work');
    RAISE EXCEPTION 'accountant_approved_supplier_credit';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.review_admin_supplier_credit_request(
      v_manager_token,'lefteria_fc_academy',v_credit,true,
      'Manager cannot approve credits');
    RAISE EXCEPTION 'location_manager_approved_supplier_credit';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_payable_position(
      v_manager_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'manager_read_supplier_credit_notes';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_credit_request(
      v_first_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
      'FIRST-CREDIT',v_month,2000,'Another organization tried');
    RAISE EXCEPTION 'first_team_accountant_created_academy_credit';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_credit_request(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_ft_bill,
      'WRONG-BILL',v_month,1000,'Wrong team supplier credit');
    RAISE EXCEPTION 'academy_accountant_credited_first_team_bill';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_credit_request(
      v_acc_token,'lefteria_fc_academy',v_req,v_bill,
      'CI-CREDIT-'||substr(md5(v_req::text),1,8),
      v_month,4300,'Different amount attempted');
    RAISE EXCEPTION 'credit_replay_changed_amount';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_credit_request(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
      'CI-CREDIT-'||substr(md5(v_req::text),1,8),
      v_month,4300,'Duplicate supplier credit note');
    RAISE EXCEPTION 'duplicate_supplier_credit_reference_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  v_result:=finance.review_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',v_credit,true,
    'Checked actual supplier credit');
  IF v_result->>'status'<>'approved_manual'
    OR v_result->>'bank_refund_recorded'<>'false'
    OR v_result->>'posted_to_general_ledger'<>'false'
  THEN RAISE EXCEPTION 'approved_supplier_credit_claimed_cash_or_gl'; END IF;
  v_result:=finance.review_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',v_credit,true,
    'Checked actual supplier credit');
  IF v_result->>'replay'<>'true'
  THEN RAISE EXCEPTION 'supplier_credit_decision_replay_invalid'; END IF;
  BEGIN
    PERFORM finance.review_admin_supplier_credit_request(
      v_super_token,'lefteria_fc_academy',v_credit,false,
      'Changing prior supplier review');
    RAISE EXCEPTION 'supplier_credit_decision_changed';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_credit_cents}')::bigint<>4500
    OR (v_report#>>'{summary,net_payables_cents}')::bigint<>9000
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>4000
    OR (v_report->'bills'->0->>'approved_credit_cents')::bigint<>4500
    OR (v_report->'bills'->0->>'remaining_cents')::bigint<>4000
    OR v_report->>'bank_refund_recorded'<>'false'
  THEN RAISE EXCEPTION 'supplier_credit_net_payables_incorrect'; END IF;

  v_second_payment:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    4001,v_month,'cash','CI cash voucher 4001')->>'id')::uuid;
  BEGIN
    PERFORM finance.review_admin_supplier_payment_request(
      v_super_token,'lefteria_fc_academy',v_second_payment,true,
      'Overpayment must be rejected');
    RAISE EXCEPTION 'approved_supplier_credit_failed_to_limit_payment';
  EXCEPTION WHEN check_violation THEN NULL; END;

  v_duplicate:=(finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    'CREDIT-EXCESS',v_month,4001,'Excess credit after settlement')->>'id')::uuid;
  BEGIN
    PERFORM finance.review_admin_supplier_credit_request(
      v_super_token,'lefteria_fc_academy',v_duplicate,true,
      'Excess adjustment should fail');
    RAISE EXCEPTION 'credit_approval_exceeded_remaining_payable';
  EXCEPTION WHEN check_violation THEN NULL; END;
  PERFORM finance.review_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',v_duplicate,false,
    'Rejected because value excessive');

  v_duplicate:=(finance.create_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',gen_random_uuid(),v_bill,
    'CREDIT-SELF',v_month,2000,'Super Admin issued credit request')->>'id')::uuid;
  BEGIN
    PERFORM finance.review_admin_supplier_credit_request(
      v_super_token,'lefteria_fc_academy',v_duplicate,true,
      'Same reviewer forbidden');
    RAISE EXCEPTION 'super_admin_approved_own_credit_note';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  PERFORM finance.review_admin_supplier_credit_request(
    v_second_token,'lefteria_fc_academy',v_duplicate,true,
    'Independent second super review');
  v_report:=finance.read_admin_supplier_payable_position(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF (v_report#>>'{summary,approved_credit_cents}')::bigint<>6500
    OR (v_report#>>'{summary,remaining_cents}')::bigint<>2000
  THEN RAISE EXCEPTION 'second_credit_not_reflected'; END IF;

  BEGIN
    UPDATE finance.supplier_credit_requests SET amount_cents=1
      WHERE id=v_credit;
    RAISE EXCEPTION 'supplier_credit_immutable_request_broken';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    DELETE FROM finance.supplier_credit_decisions WHERE request_id=v_credit;
    RAISE EXCEPTION 'supplier_credit_immutable_decision_broken';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_acc_token;
  BEGIN
    PERFORM finance.read_admin_supplier_payable_position(
      v_acc_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'revoked_accountant_read_credits';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  IF NOT EXISTS(SELECT 1 FROM audit.security_events
    WHERE action='admin.finance.supplier_credit.request'
      AND actor_identity_id=v_acc AND organization_id='lefteria_fc_academy')
    OR NOT EXISTS(SELECT 1 FROM audit.security_events
    WHERE action='admin.finance.supplier_credit.review'
      AND actor_identity_id=v_super AND organization_id='lefteria_fc_academy')
  THEN RAISE EXCEPTION 'supplier_credit_missing_audit_event'; END IF;
END $credit_checks$;
ROLLBACK;
