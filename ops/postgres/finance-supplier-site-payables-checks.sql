\set ON_ERROR_STOP on
\set ECHO none
-- Disposable synthetic data only; rolls back invoices, credits, sessions, grants.
BEGIN;
DO $site_payables_checks$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_second_super uuid:=gen_random_uuid();
  v_accountant uuid:=gen_random_uuid();
  v_manager_a uuid:=gen_random_uuid();
  v_manager_a2 uuid:=gen_random_uuid();
  v_first_accountant uuid:=gen_random_uuid();
  v_city_a uuid:=gen_random_uuid(); v_city_b uuid:=gen_random_uuid();
  v_site_a uuid:=gen_random_uuid();
  v_site_a2 uuid:=gen_random_uuid();
  v_site_b uuid:=gen_random_uuid();
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_second_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_acc_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager2_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_first_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_bill_a uuid; v_bill_a2 uuid; v_bill_b uuid; v_bill_first uuid;
  v_pay uuid; v_credit uuid;
  v_payment_a uuid; v_payment_a2 uuid;
  v_credit_a uuid; v_credit_a2 uuid;
  v_reverse_payment uuid; v_reverse_credit uuid;
  v_report jsonb; v_row jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled) VALUES
    (v_super,'ci-site-pay-super-'||substr(md5(v_super::text),1,8),
     'CI Site Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_second_super,'ci-site-pay-super2-'||substr(md5(v_second_super::text),1,8),
     'CI Independent Second Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_accountant,'ci-site-pay-acc-'||substr(md5(v_accountant::text),1,8),
     'CI Site Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_manager_a,'ci-site-pay-manager-'||substr(md5(v_manager_a::text),1,8),
     'CI Site Manager A','manager',true,'ci-fixture-hash',false,false),
    (v_manager_a2,'ci-site-pay-manager2-'||substr(md5(v_manager_a2::text),1,8),
     'CI Site Manager A2','manager',true,'ci-fixture-hash',false,false),
    (v_first_accountant,'ci-site-pay-first-'||substr(md5(v_first_accountant::text),1,8),
     'CI First Accountant','accountant',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES (v_super,'lefteria_fc_academy'),(v_super,'lefteria_fc'),
      (v_second_super,'lefteria_fc_academy'),
      (v_accountant,'lefteria_fc_academy'),
      (v_manager_a,'lefteria_fc_academy'),
      (v_manager_a2,'lefteria_fc_academy'),
      (v_first_accountant,'lefteria_fc');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,
    credential_digest,created_at,expires_at)
    VALUES
      (gen_random_uuid(),v_super_token,v_super,repeat('a',64),
       clock_timestamp(),clock_timestamp()+interval '1 day'),
      (gen_random_uuid(),v_second_super_token,v_second_super,repeat('f',64),
       clock_timestamp(),clock_timestamp()+interval '1 day'),
      (gen_random_uuid(),v_acc_token,v_accountant,repeat('b',64),
       clock_timestamp(),clock_timestamp()+interval '1 day'),
      (gen_random_uuid(),v_manager_token,v_manager_a,repeat('c',64),
       clock_timestamp(),clock_timestamp()+interval '1 day'),
      (gen_random_uuid(),v_manager2_token,v_manager_a2,repeat('d',64),
       clock_timestamp(),clock_timestamp()+interval '1 day'),
      (gen_random_uuid(),v_first_token,v_first_accountant,repeat('e',64),
       clock_timestamp(),clock_timestamp()+interval '1 day');

  INSERT INTO club.football_cities(id,name,slug) VALUES
    (v_city_a,'CI Payable City A','payable-a-'||substr(md5(v_city_a::text),1,8)),
    (v_city_b,'CI Payable City B','payable-b-'||substr(md5(v_city_b::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug) VALUES
    (v_site_a,v_city_a,'CI Payable Site A','site-a'),
    (v_site_a2,v_city_a,'CI Payable Site A2','site-a2'),
    (v_site_b,v_city_b,'CI Payable Site B','site-b');
  INSERT INTO club.football_operational_scopes(id,organization_id,academy_city_id)
    VALUES
      (gen_random_uuid(),'lefteria_fc_academy',v_city_a),
      (gen_random_uuid(),'lefteria_fc_academy',v_city_b);
  INSERT INTO club.football_location_admin_grants(
    admin_account_id,location_id,role)
    VALUES (v_manager_a,v_site_a,'location_manager'),
      (v_manager_a2,v_site_a2,'location_manager');

  v_bill_a:=(finance.create_admin_supplier_bill(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Private Vendor A','PAYABLE-A-'||substr(md5(v_site_a::text),1,8),
    'equipment','Synthetic site A materials',v_month,13500,v_site_a)->>'id')::uuid;
  v_bill_a2:=(finance.create_admin_supplier_bill(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Private Vendor A2','PAYABLE-A2-'||substr(md5(v_site_a2::text),1,8),
    'facilities','Synthetic site A2 rent',v_month,7000,v_site_a2)->>'id')::uuid;
  v_bill_b:=(finance.create_admin_supplier_bill(
    v_super_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Private Vendor B','PAYABLE-B-'||substr(md5(v_site_b::text),1,8),
    'facilities','Synthetic other city rent',v_month,24000,v_site_b)->>'id')::uuid;
  v_bill_first:=(finance.create_admin_supplier_bill(
    v_first_token,'lefteria_fc',gen_random_uuid(),
    'CI First Team Private Vendor','PAYABLE-FT-'||substr(md5(v_bill_a::text),1,8),
    'travel','Synthetic First Team bus',v_month,30000,NULL)->>'id')::uuid;

  v_pay:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a,5000,v_month,'bank_transfer','CI Pay Evidence A')->>'id')::uuid;
  v_payment_a:=v_pay;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_pay,true,
    'CI manually approved payment');
  PERFORM finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a,2000,v_month,'bank_transfer','CI Pending Payment');
  v_pay:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a,750,v_month,'cash','CI Rejected Voucher')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_pay,false,
    'CI rejected proof reason');

  v_credit:=(finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a,'CREDIT-SITE-A',v_month,4500,
    'CI vendor material credit')->>'id')::uuid;
  v_credit_a:=v_credit;
  PERFORM finance.review_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',v_credit,true,
    'CI approved verified credit');
  PERFORM finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a,'CREDIT-PENDING',v_month,1000,
    'CI pending credit note');
  v_credit:=(finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a,'CREDIT-REJECT',v_month,1000,
    'CI rejected credit note')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',v_credit,false,
    'CI invalid supplier document');

  v_credit:=(finance.create_admin_supplier_credit_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a2,'CREDIT-A2',v_month,1000,
    'CI other site reduction')->>'id')::uuid;
  v_credit_a2:=v_credit;
  PERFORM finance.review_admin_supplier_credit_request(
    v_super_token,'lefteria_fc_academy',v_credit,true,
    'CI other site credit verified');
  v_pay:=(finance.create_admin_supplier_payment_request(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    v_bill_a2,1000,v_month,'cash','CI other site voucher')->>'id')::uuid;
  v_payment_a2:=v_pay;
  PERFORM finance.review_admin_supplier_payment_request(
    v_super_token,'lefteria_fc_academy',v_pay,true,
    'CI other site voucher reviewed');

  v_report:=finance.read_admin_supplier_site_payables(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF v_report->>'organization_id'<>'lefteria_fc_academy'
    OR v_report->>'currency'<>'EUR'
    OR v_report->>'reporting_basis'<>'supplier_bill_date'
    OR v_report->>'bank_verified'<>'false'
    OR v_report->>'is_cash_payment'<>'false'
    OR v_report->>'posted_to_general_ledger'<>'false'
    OR jsonb_array_length(v_report->'locations')<>1
    OR v_report::text LIKE '%CI Private%'
    OR v_report::text LIKE '%PAYABLE-%'
    OR v_report::text LIKE '%CREDIT-%'
    OR v_report::text LIKE '%CI Pay Evidence%'
    OR v_report::text LIKE '%invoice_reference%'
    OR v_report::text LIKE '%credit_reference%'
    OR v_report::text LIKE '%'||v_site_a2::text||'%'
    OR v_report::text LIKE '%'||v_site_b::text||'%'
    OR v_report::text LIKE '%'||v_city_b::text||'%'
  THEN RAISE EXCEPTION 'location_manager_payable_scope_or_redaction_failed'; END IF;
  v_row:=v_report->'locations'->0;
  IF (v_row->>'location_id')::uuid<>v_site_a
    OR (v_row->>'city_id')::uuid<>v_city_a
    OR (v_row->>'bill_count')::int<>1
    OR (v_row->>'gross_amount_cents')::bigint<>13500
    OR (v_row->>'approved_credit_cents')::bigint<>4500
    OR (v_row->>'pending_credit_cents')::bigint<>1000
    OR (v_row->>'net_payables_cents')::bigint<>9000
    OR (v_row->>'approved_manual_payment_cents')::bigint<>5000
    OR (v_row->>'pending_payment_evidence_cents')::bigint<>2000
    OR (v_row->>'remaining_cents')::bigint<>4000
  THEN RAISE EXCEPTION 'location_manager_payable_balances_incorrect'; END IF;

  v_report:=finance.read_admin_supplier_site_payables(
    v_manager2_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_report->'locations')<>1
    OR (v_report->'locations'->0->>'location_id')::uuid<>v_site_a2
    OR (v_report->'locations'->0->>'gross_amount_cents')::bigint<>7000
    OR (v_report->'locations'->0->>'approved_credit_cents')::bigint<>1000
    OR (v_report->'locations'->0->>'approved_manual_payment_cents')::bigint<>1000
    OR (v_report->'locations'->0->>'remaining_cents')::bigint<>5000
    OR v_report::text LIKE '%'||v_site_a::text||'%'
    OR v_report::text LIKE '%'||v_site_b::text||'%'
  THEN RAISE EXCEPTION 'same_city_other_location_payables_exposed'; END IF;

  v_report:=finance.read_admin_supplier_site_payables(
    v_super_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_report->'locations')<>3
    OR NOT EXISTS (
      SELECT 1 FROM jsonb_array_elements(v_report->'locations') AS loc(item)
      WHERE (loc.item->>'location_id')::uuid=v_site_b
        AND (loc.item->>'gross_amount_cents')::bigint=24000
        AND (loc.item->>'remaining_cents')::bigint=24000)
    OR v_report::text LIKE '%CI Private%'
  THEN RAISE EXCEPTION 'super_admin_payable_site_breakdown_wrong'; END IF;

  BEGIN
    PERFORM finance.read_admin_supplier_site_payables(
      v_first_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'first_team_accountant_viewed_academy_site';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_site_payables(
      v_super_token,'lefteria_fc',v_month);
    RAISE EXCEPTION 'first_team_took_academy_site_payables';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_site_payables(
      v_manager_token,'lefteria_fc_academy',v_month+1);
    RAISE EXCEPTION 'non_first_day_month_allowed';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_site_payables(
      v_manager_token,'lefteria_fc_academy',NULL);
    RAISE EXCEPTION 'null_month_allowed';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;


  -- Pending reversal preserves the original effective amount.
  v_reverse_payment:=(finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'payment',v_payment_a,'Duplicated off-system voucher needs independent recheck')->>'id')::uuid;
  v_report:=finance.read_admin_supplier_site_payables(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF (v_report->'locations'->0->>'approved_manual_payment_cents')::bigint<>5000
    OR (v_report->'locations'->0->>'remaining_cents')::bigint<>4000
  THEN RAISE EXCEPTION 'pending_reversal_changed_site_payables'; END IF;

  -- Neither the requester nor the original approving Super Admin can
  -- approve a reversal. Approval must be truly independent.
  BEGIN
    PERFORM finance.review_admin_supplier_approval_reversal(
      v_super_token,'lefteria_fc_academy',v_reverse_payment,true,
      'Original approver must not reverse own approval');
    RAISE EXCEPTION 'site_original_payment_reviewer_approved_reversal';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  PERFORM finance.review_admin_supplier_approval_reversal(
    v_second_super_token,'lefteria_fc_academy',v_reverse_payment,true,
    'Independent verification of duplicate evidence');
  v_report:=finance.read_admin_supplier_site_payables(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF v_report->>'approval_reversal_adjusted'<>'true'
    OR jsonb_array_length(v_report->'locations')<>1
    OR (v_report->'locations'->0->>'approved_manual_payment_cents')::bigint<>0
    OR (v_report->'locations'->0->>'approved_credit_cents')::bigint<>4500
    OR (v_report->'locations'->0->>'remaining_cents')::bigint<>9000
    OR (v_report->'locations'->0->>'pending_payment_evidence_cents')::bigint<>2000
    OR v_report::text LIKE '%Duplicated off-system voucher%'
    OR v_report::text LIKE '%'||v_site_a2::text||'%'
  THEN RAISE EXCEPTION 'payment_reversal_not_applied_to_only_own_site'; END IF;

  -- Rejecting a proposed correction does not remove an approved credit.
  v_reverse_credit:=(finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'credit',v_credit_a,'Supplier credit is disputed but not yet invalidated')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_approval_reversal(
    v_second_super_token,'lefteria_fc_academy',v_reverse_credit,false,
    'Dispute was resolved without changing credit');
  v_report:=finance.read_admin_supplier_site_payables(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF (v_report->'locations'->0->>'approved_credit_cents')::bigint<>4500
    OR (v_report->'locations'->0->>'remaining_cents')::bigint<>9000
  THEN RAISE EXCEPTION 'rejected_credit_reversal_changed_site_payables'; END IF;

  v_reverse_credit:=(finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'credit',v_credit_a,'Supplier later cancelled the previously approved credit')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_approval_reversal(
    v_second_super_token,'lefteria_fc_academy',v_reverse_credit,true,
    'Independently confirmed the credit cancellation');
  v_report:=finance.read_admin_supplier_site_payables(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_report->'locations')<>1
    OR (v_report->'locations'->0->>'gross_amount_cents')::bigint<>13500
    OR (v_report->'locations'->0->>'approved_credit_cents')::bigint<>0
    OR (v_report->'locations'->0->>'pending_credit_cents')::bigint<>1000
    OR (v_report->'locations'->0->>'approved_manual_payment_cents')::bigint<>0
    OR (v_report->'locations'->0->>'remaining_cents')::bigint<>13500
    OR v_report::text LIKE '%supplier_name%'
    OR v_report::text LIKE '%review_note%'
    OR v_report::text LIKE '%'||v_site_a2::text||'%'
    OR v_report::text LIKE '%'||v_site_b::text||'%'
  THEN RAISE EXCEPTION 'credit_reversal_not_applied_to_only_own_site'; END IF;

  -- Reversal of site A2's approved payment must leave site A's original
  -- gross and outstanding position unchanged, even within the same city.
  v_reverse_payment:=(finance.create_admin_supplier_approval_reversal(
    v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
    'payment',v_payment_a2,'Second location voucher requires manual correction')->>'id')::uuid;
  PERFORM finance.review_admin_supplier_approval_reversal(
    v_second_super_token,'lefteria_fc_academy',v_reverse_payment,true,
    'Second site correction reviewed independently');
  v_report:=finance.read_admin_supplier_site_payables(
    v_manager2_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_report->'locations')<>1
    OR (v_report->'locations'->0->>'location_id')::uuid<>v_site_a2
    OR (v_report->'locations'->0->>'approved_manual_payment_cents')::bigint<>0
    OR (v_report->'locations'->0->>'approved_credit_cents')::bigint<>1000
    OR (v_report->'locations'->0->>'remaining_cents')::bigint<>6000
    OR v_report::text LIKE '%'||v_site_a::text||'%'
    OR v_report::text LIKE '%'||v_site_b::text||'%'
  THEN RAISE EXCEPTION 'same_city_other_location_reversal_leaked'; END IF;
  v_report:=finance.read_admin_supplier_site_payables(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF (v_report->'locations'->0->>'remaining_cents')::bigint<>13500
  THEN RAISE EXCEPTION 'reversal_in_other_site_changed_my_balance'; END IF;

  -- Academy-wide authorized Super Admin aggregates are still segmented;
  -- First Team finance is outside this report.
  v_report:=finance.read_admin_supplier_site_payables(
    v_super_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_report->'locations')<>3
    OR NOT EXISTS (
      SELECT 1 FROM jsonb_array_elements(v_report->'locations') loc(item)
      WHERE (loc.item->>'location_id')::uuid=v_site_b
        AND (loc.item->>'gross_amount_cents')::bigint=24000
        AND (loc.item->>'remaining_cents')::bigint=24000)
    OR v_report::text LIKE '%CI First Team Private Vendor%'
  THEN RAISE EXCEPTION 'academy_reversal_changed_first_team_or_other_city'; END IF;

  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_manager_token;
  BEGIN
    PERFORM finance.read_admin_supplier_site_payables(
      v_manager_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'revoked_site_manager_read_payables';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $site_payables_checks$;
ROLLBACK;
