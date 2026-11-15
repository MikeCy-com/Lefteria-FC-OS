\set ON_ERROR_STOP on
\set ECHO none
-- Disposable integration proof. No provider calls or real customer records.
BEGIN;
DO $accounting_checks$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_accountant uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_outsider uuid:=gen_random_uuid();
  v_guardian uuid:=gen_random_uuid();
  v_player text:='ci-overview-'||md5(gen_random_uuid()::text);
  v_fee uuid:=gen_random_uuid();
  v_charge uuid:=gen_random_uuid();
  v_month date:=date_trunc('month',clock_timestamp() AT TIME ZONE 'Asia/Nicosia')::date;
  v_super_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_accountant_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_outsider_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_data jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled) VALUES
    (v_super,'ci-overview-super-'||substr(md5(v_super::text),1,8),
      'CI Finance Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_accountant,'ci-overview-accountant-'||substr(md5(v_accountant::text),1,8),
      'CI Finance Accountant','accountant',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-overview-manager-'||substr(md5(v_manager::text),1,8),
      'CI Finance Manager','manager',true,'ci-fixture-hash',false,false),
    (v_outsider,'ci-overview-outsider-'||substr(md5(v_outsider::text),1,8),
      'CI Finance Outsider','accountant',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES(v_super,'lefteria_fc_academy'),(v_super,'lefteria_fc'),
    (v_accountant,'lefteria_fc_academy'),(v_manager,'lefteria_fc_academy'),
    (v_outsider,'lefteria_fc');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,
    credential_digest,created_at,expires_at) VALUES
    (gen_random_uuid(),v_super_token,v_super,repeat('a',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_accountant_token,v_accountant,repeat('b',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('c',64),clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_outsider_token,v_outsider,repeat('d',64),clock_timestamp(),clock_timestamp()+interval '1 day');

  INSERT INTO identity.mobile_identities(id,normalized_phone,role,display_name,
    is_active,platform,created_at)
  VALUES(v_guardian,'35799123456','parent','CI Guardian',
    true,'mobile',clock_timestamp());
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
  VALUES(v_player,'lefteria_fc_academy','CI Overview Player',1,'Forward',
    'Academy',true,false,false,true);
  INSERT INTO finance.training_fee_issues(id,organization_id,player_id,
    payer_identity_id,fee_month,amount_cents,payer_phone,payer_email,
    payer_first_name,payer_last_name,issued_by)
  VALUES(v_fee,'lefteria_fc_academy',v_player,v_guardian,v_month,
    4800,'+35799123456','ci@example.invalid','CI','Guardian',v_super);
  INSERT INTO finance.general_charge_issues(id,organization_id,player_id,
    charge_type,description,amount_cents,issued_at,created_by)
  VALUES(v_charge,'lefteria_fc_academy',v_player,'equipment',
    'CI training supplies',8000,clock_timestamp(),v_super);
  INSERT INTO finance.general_charge_events(id,charge_id,organization_id,
    idempotency_id,event_kind,payment_method,evidence_type,amount_cents,actor_id)
  VALUES(gen_random_uuid(),v_charge,'lefteria_fc_academy',gen_random_uuid(),
    'payment','cash','admin_cash',2000,v_super);

  v_data:=finance.read_admin_accounting_overview(
    v_accountant_token,'lefteria_fc_academy',v_month);
  IF v_data->>'organization_id'<>'lefteria_fc_academy'
    OR v_data->>'scope'<>'organization'
    OR (v_data->>'is_statutory_statement')::boolean<>false
    OR (v_data#>>'{academy_training_fees,issued_cents}')::bigint<>4800
    OR (v_data#>>'{academy_training_fees,open_cents}')::bigint<>4800
    OR (v_data#>>'{general_charges,issued_cents}')::bigint<>8000
    OR (v_data#>>'{general_charges,collected_cents}')::bigint<>2000
    OR (v_data#>>'{general_charges,outstanding_cents}')::bigint<>6000
    OR v_data::text ~ '(payer_phone|payer_email|player_id|guardian)'
  THEN RAISE EXCEPTION 'accountant_overview_invalid_or_leaked_personal_data'; END IF;

  v_data:=finance.read_admin_accounting_overview(
    v_super_token,'lefteria_fc',v_month);
  IF v_data->>'organization_id'<>'lefteria_fc'
    OR (v_data#>>'{academy_training_fees,issued_count}')::int<>0
    OR (v_data#>>'{general_charges,issued_count}')::int<>0
  THEN RAISE EXCEPTION 'first_team_academy_finance_leak'; END IF;
  BEGIN
    PERFORM finance.read_admin_accounting_overview(
      v_manager_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'location_manager_received_organization_finance';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_accounting_overview(
      v_outsider_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'accountant_other_organization_leak';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_accountant_token;
  BEGIN
    PERFORM finance.read_admin_accounting_overview(
      v_accountant_token,'lefteria_fc_academy',v_month);
    RAISE EXCEPTION 'revoked_accountant_retrieved_finances';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $accounting_checks$;
ROLLBACK;
