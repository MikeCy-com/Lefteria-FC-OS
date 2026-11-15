\set ON_ERROR_STOP on
\set ECHO none
-- Synthetic isolated database test; automatic ROLLBACK, no financial provider calls.
BEGIN;
DO $supplier_checks$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_accountant uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_first uuid:=gen_random_uuid();
  v_outsider uuid:=gen_random_uuid();
  v_city_a uuid:=gen_random_uuid();
  v_city_b uuid:=gen_random_uuid();
  v_site_a uuid:=gen_random_uuid();
  v_site_b uuid:=gen_random_uuid();
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_acc_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_first_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_outsider_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_request uuid:=gen_random_uuid();
  v_data jsonb; v_bill jsonb; v_id uuid;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled) VALUES
    (v_super,'ci-sup-super-'||substr(md5(v_super::text),1,8),'CI Super',
      'super_admin',true,'ci-fixture-hash',false,false),
    (v_accountant,'ci-sup-accountant-'||substr(md5(v_accountant::text),1,8),
      'CI Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-sup-manager-'||substr(md5(v_manager::text),1,8),
      'CI Manager','manager',true,'ci-fixture-hash',false,false),
    (v_first,'ci-sup-first-'||substr(md5(v_first::text),1,8),
      'CI First Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_outsider,'ci-sup-outsider-'||substr(md5(v_outsider::text),1,8),
      'CI Outside Accountant','accountant',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(
    admin_account_id,organization_id) VALUES
    (v_super,'lefteria_fc'),(v_super,'lefteria_fc_academy'),
    (v_accountant,'lefteria_fc_academy'),(v_manager,'lefteria_fc_academy'),
    (v_first,'lefteria_fc'),(v_outsider,'lefteria_fc');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,
    credential_digest,created_at,expires_at) VALUES
    (gen_random_uuid(),v_super_token,v_super,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_acc_token,v_accountant,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('c',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_first_token,v_first,repeat('d',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_outsider_token,v_outsider,repeat('e',64),clock_timestamp(),clock_timestamp()+interval '1 day');

  INSERT INTO club.football_cities(id,name,slug) VALUES
    (v_city_a,'CI Supplier City A','supplier-city-a-'||substr(md5(v_city_a::text),1,8)),
    (v_city_b,'CI Supplier City B','supplier-city-b-'||substr(md5(v_city_b::text),1,8));
  INSERT INTO club.football_locations(id,city_id,name,slug) VALUES
    (v_site_a,v_city_a,'CI Supplier North','north'),
    (v_site_b,v_city_b,'CI Supplier South','south');
  INSERT INTO club.football_operational_scopes(
    id,organization_id,academy_city_id) VALUES
    (gen_random_uuid(),'lefteria_fc_academy',v_city_a),
    (gen_random_uuid(),'lefteria_fc_academy',v_city_b);
  INSERT INTO club.football_location_admin_grants(
    admin_account_id,location_id,role)
  VALUES(v_manager,v_site_a,'location_manager');

  v_bill:=finance.create_admin_supplier_bill(
    v_acc_token,'lefteria_fc_academy',v_request,'CI Vendor North',
    'AC-N-'||substr(md5(v_request::text),1,8),'equipment','Training cones',
    v_month,13500,v_site_a);
  v_id:=(v_bill->>'id')::uuid;
  IF v_bill->>'organization_id'<>'lefteria_fc_academy'
    OR v_bill->>'recorded'<>'true' OR v_bill->>'replay'<>'false'
    OR v_bill->>'cash_paid'<>'false'
    OR v_bill->>'posted_to_general_ledger'<>'false'
  THEN RAISE EXCEPTION 'supplier_bill_false_payment_claim'; END IF;

  v_bill:=finance.create_admin_supplier_bill(
    v_acc_token,'lefteria_fc_academy',v_request,'CI Vendor North',
    'AC-N-'||substr(md5(v_request::text),1,8),'equipment','Training cones',
    v_month,13500,v_site_a);
  IF (v_bill->>'id')::uuid<>v_id OR v_bill->>'replay'<>'true'
    OR (SELECT count(*) FROM finance.supplier_bills
       WHERE organization_id='lefteria_fc_academy' AND id=v_id)<>1
  THEN RAISE EXCEPTION 'supplier_bill_idempotency_broken'; END IF;

  BEGIN
    PERFORM finance.create_admin_supplier_bill(
      v_acc_token,'lefteria_fc_academy',v_request,'CI Vendor North',
      'AC-N-'||substr(md5(v_request::text),1,8),'equipment',
      'Different contract',v_month,13500,v_site_a);
    RAISE EXCEPTION 'supplier_bill_request_mutated';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  BEGIN
    PERFORM finance.create_admin_supplier_bill(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),'CI Vendor North',
      'AC-N-'||substr(md5(v_request::text),1,8),'equipment',
      'Different invoice',v_month,2000,v_site_a);
    RAISE EXCEPTION 'duplicate_vendor_invoice_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;

  PERFORM finance.create_admin_supplier_bill(
    v_super_token,'lefteria_fc_academy',gen_random_uuid(),
    'CI Vendor South','AC-S-'||substr(md5(v_request::text),1,8),
    'facilities','South facility rental',v_month,24000,v_site_b);
  PERFORM finance.create_admin_supplier_bill(
    v_first_token,'lefteria_fc',gen_random_uuid(),
    'CI Vendor First Team','FT-'||substr(md5(v_request::text),1,8),
    'travel','First team travel',v_month,48000,NULL);

  v_data:=finance.read_admin_supplier_bills(
    v_acc_token,'lefteria_fc_academy',v_month);
  IF v_data->>'organization_id'<>'lefteria_fc_academy'
    OR (v_data#>>'{summary,bill_count}')::int<>2
    OR (v_data#>>'{summary,gross_amount_cents}')::bigint<>37500
    OR jsonb_array_length(v_data->'bills')<>2
    OR jsonb_array_length(v_data->'sites')<>2
    OR v_data::text LIKE '%CI Vendor First Team%'
    OR v_data->>'is_cash_payment'<>'false'
  THEN RAISE EXCEPTION 'academy_accountant_supplier_view_invalid'; END IF;

  v_data:=finance.read_admin_supplier_bills(
    v_first_token,'lefteria_fc',v_month);
  IF v_data->>'organization_id'<>'lefteria_fc'
    OR (v_data#>>'{summary,gross_amount_cents}')::bigint<>48000
    OR jsonb_array_length(v_data->'sites')<>0
    OR v_data::text LIKE '%CI Vendor North%'
  THEN RAISE EXCEPTION 'first_team_academy_supplier_leak'; END IF;

  v_data:=finance.read_admin_supplier_site_totals(
    v_manager_token,'lefteria_fc_academy',v_month);
  IF jsonb_array_length(v_data->'locations')<>1
    OR (v_data->'locations'->0->>'location_id')::uuid<>v_site_a
    OR (v_data->'locations'->0->>'gross_amount_cents')::bigint<>13500
    OR v_data::text LIKE '%CI Vendor%'
    OR v_data::text LIKE '%invoice_reference%'
    OR v_data::text LIKE '%24000%'
  THEN RAISE EXCEPTION 'site_manager_supplier_leak'; END IF;

  BEGIN
    PERFORM finance.read_admin_supplier_bills(
      v_manager_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'manager_read_vendor_invoice_details';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_bill(
      v_manager_token,'lefteria_fc_academy',gen_random_uuid(),
      'CI Vendor Denied','DENIED','other','Denied expense',v_month,1000,v_site_a);
    RAISE EXCEPTION 'manager_recorded_supplier_bill';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_bills(
      v_first_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'first_team_accountant_read_academy_bills';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_bill(
      v_acc_token,'lefteria_fc_academy',gen_random_uuid(),
      'CI Vendor Missing','MISSING','other','No site bill',v_month,1000,NULL);
    RAISE EXCEPTION 'academy_bill_without_location';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM finance.create_admin_supplier_bill(
      v_first_token,'lefteria_fc',gen_random_uuid(),
      'CI Vendor Wrong','WRONG','other','First team site bill',v_month,1000,v_site_a);
    RAISE EXCEPTION 'first_team_took_academy_site';
  EXCEPTION WHEN invalid_parameter_value THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_supplier_site_totals(
      v_outsider_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'outsider_got_academy_site_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_acc_token;
  BEGIN
    PERFORM finance.read_admin_supplier_bills(
      v_acc_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'revoked_accountant_read_vendor_records';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  IF NOT EXISTS(SELECT 1 FROM audit.security_events
    WHERE actor_identity_id=v_accountant AND
      action='admin.finance.supplier_bill.record' AND
      organization_id='lefteria_fc_academy')
  THEN RAISE EXCEPTION 'supplier_bill_missing_audit'; END IF;
END $supplier_checks$;
ROLLBACK;
