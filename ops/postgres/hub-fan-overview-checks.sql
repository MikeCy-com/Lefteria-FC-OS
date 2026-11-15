\set ON_ERROR_STOP on
\set ECHO none

-- Disposable PostgreSQL acceptance: every row and role exists inside this
-- transaction only. No Payabl HTTP call, real customer data or production DB.
BEGIN;
DO $hub_fan_verified_overview$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_other_super uuid:=gen_random_uuid();
  v_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_other_token text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_good uuid:=gen_random_uuid();
  v_refund uuid:=gen_random_uuid();
  v_fake uuid:=gen_random_uuid();
  v_wrong uuid:=gen_random_uuid();
  v_inactive uuid:=gen_random_uuid();
  v_old uuid:=gen_random_uuid();
  v_pending uuid:=gen_random_uuid();
  v_past uuid:=gen_random_uuid();
  v_cancelled uuid:=gen_random_uuid();
  v_team uuid:=gen_random_uuid();
  v_cross uuid:=gen_random_uuid();
  v_good_payment uuid:=gen_random_uuid();
  v_refund_payment uuid:=gen_random_uuid();
  v_fake_payment uuid:=gen_random_uuid();
  v_wrong_payment uuid:=gen_random_uuid();
  v_inactive_payment uuid:=gen_random_uuid();
  v_old_payment uuid:=gen_random_uuid();
  v_team_payment uuid:=gen_random_uuid();
  v_cross_payment uuid:=gen_random_uuid();
  v_data jsonb;
  v_team_data jsonb;
  v_capture boolean;
BEGIN
  -- Use only synthetic, non-bcrypt fixture credentials and throwaway accounts.
  INSERT INTO identity.admin_accounts
    (id,username,display_name,role,is_active,password_hash,invite_pending,otp2fa_enabled)
  VALUES
    (v_super,'ci-fan-super-'||left(md5(v_super::text),8),
      'CI Fan Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-fan-manager-'||left(md5(v_manager::text),8),
      'CI Fan Location Manager','manager',true,'ci-fixture-hash',false,false),
    (v_other_super,'ci-fan-other-'||left(md5(v_other_super::text),8),
      'CI Fan Unassigned Super','super_admin',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
  VALUES (v_super,'lefteria_fc_academy'),(v_super,'lefteria_fc'),
    (v_manager,'lefteria_fc_academy'),(v_other_super,'lefteria_fc');
  INSERT INTO identity.admin_sessions
    (id,token_digest,admin_account_id,credential_digest,created_at,expires_at)
  VALUES
    (gen_random_uuid(),v_token,v_super,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_manager_token,v_manager,repeat('b',64),
      clock_timestamp(),clock_timestamp()+interval '1 day'),
    (gen_random_uuid(),v_other_token,v_other_super,repeat('c',64),
      clock_timestamp(),clock_timestamp()+interval '1 day');

  INSERT INTO identity.mobile_identities
    (id,normalized_phone,role,display_name,is_active,platform,created_at)
  VALUES
    (v_good,'35799670101','fan','CI paid fan',true,'mobile',clock_timestamp()),
    (v_refund,'35799670102','fan','CI refunded fan',true,'mobile',clock_timestamp()),
    (v_fake,'35799670103','fan','CI unpaid fan',true,'mobile',clock_timestamp()),
    (v_wrong,'35799670104','fan','CI mismatched actor',true,'mobile',clock_timestamp()),
    (v_inactive,'35799670105','fan','CI disabled fan',false,'mobile',clock_timestamp()),
    (v_old,'35799670106','fan','CI expired fan',true,'mobile',clock_timestamp()),
    (v_pending,'35799670107','fan','CI pending fan',true,'mobile',clock_timestamp()),
    (v_past,'35799670108','fan','CI past due fan',true,'mobile',clock_timestamp()),
    (v_cancelled,'35799670109','fan','CI cancelled fan',true,'mobile',clock_timestamp()),
    (v_team,'35799670110','fan','CI first team fan',true,'mobile',clock_timestamp()),
    (v_cross,'35799670111','fan','CI crossed ownership',true,'mobile',clock_timestamp());

  -- Prices are independent and disabled plans are still visible to Super Admin.
  INSERT INTO hub.fan_plan_prices
    (organization_id,track,monthly_price_cents,is_enabled)
  VALUES
    ('lefteria_fc_academy','parent',1200,true),
    ('lefteria_fc_academy','both',1800,false),
    ('lefteria_fc','player',1500,true);

  INSERT INTO finance.payment_requests
    (id,organization_id,actor_id,client_request_id,order_id,purpose,
     amount_cents,currency,payer_email,payer_first_name,payer_last_name,
     callback_digest,status)
  SELECT id,owner,actor,id,'LFC'||left(replace(id::text,'-',''),27),
    purpose,cents,'EUR','ci-fan@example.invalid','CI','Fan',
    md5(id::text)||md5(id::text||'nonce'),'ready'
  FROM (VALUES
    (v_good_payment,'lefteria_fc_academy',v_good,'fan_hub_parent',1200),
    (v_refund_payment,'lefteria_fc_academy',v_refund,'fan_hub_parent',1200),
    (v_fake_payment,'lefteria_fc_academy',v_fake,'fan_hub_parent',1200),
    -- Wrong actor: Payabl request was made by a different identity.
    (v_wrong_payment,'lefteria_fc_academy',v_good,'fan_hub_parent',1200),
    (v_inactive_payment,'lefteria_fc_academy',v_inactive,'fan_hub_parent',1200),
    (v_old_payment,'lefteria_fc_academy',v_old,'fan_hub_parent',1200),
    (v_team_payment,'lefteria_fc',v_team,'fan_hub_player',1500),
    -- Cross-organization payment cannot authorize another organization's fan.
    (v_cross_payment,'lefteria_fc_academy',v_cross,'fan_hub_player',1500)
  ) AS fixture(id,owner,actor,purpose,cents);

  -- Signed callback SQL transition. These calls simulate validated provider
  -- evidence IN THE TEST TRANSACTION; they are not payment initiations.
  v_capture:=finance.apply_payabl_notification(
    v_good_payment,md5(v_good_payment::text)||md5(v_good_payment::text||'nonce'),
    '300101','capture',0,1794260000,
    'LFC'||left(replace(v_good_payment::text,'-',''),27),'',1200,'EUR');
  IF NOT v_capture THEN RAISE EXCEPTION 'synthetic good capture failed'; END IF;
  v_capture:=finance.apply_payabl_notification(
    v_refund_payment,md5(v_refund_payment::text)||md5(v_refund_payment::text||'nonce'),
    '300102','capture',0,1794260000,
    'LFC'||left(replace(v_refund_payment::text,'-',''),27),'',1200,'EUR');
  IF NOT v_capture THEN RAISE EXCEPTION 'synthetic refund capture failed'; END IF;
  v_capture:=finance.apply_payabl_notification(
    v_refund_payment,md5(v_refund_payment::text)||md5(v_refund_payment::text||'nonce'),
    '300102','refund',0,1794260001,
    'LFC'||left(replace(v_refund_payment::text,'-',''),27),'refunded',1200,'EUR');
  IF v_capture THEN RAISE EXCEPTION 'refund must not issue another capture'; END IF;
  v_capture:=finance.apply_payabl_notification(
    v_wrong_payment,md5(v_wrong_payment::text)||md5(v_wrong_payment::text||'nonce'),
    '300103','capture',0,1794260000,
    'LFC'||left(replace(v_wrong_payment::text,'-',''),27),'',1200,'EUR');
  IF NOT v_capture THEN RAISE EXCEPTION 'synthetic wrong-actor capture failed'; END IF;
  v_capture:=finance.apply_payabl_notification(
    v_inactive_payment,md5(v_inactive_payment::text)||md5(v_inactive_payment::text||'nonce'),
    '300104','capture',0,1794260000,
    'LFC'||left(replace(v_inactive_payment::text,'-',''),27),'',1200,'EUR');
  IF NOT v_capture THEN RAISE EXCEPTION 'synthetic inactive-fan capture failed'; END IF;
  v_capture:=finance.apply_payabl_notification(
    v_old_payment,md5(v_old_payment::text)||md5(v_old_payment::text||'nonce'),
    '300105','capture',0,1794260000,
    'LFC'||left(replace(v_old_payment::text,'-',''),27),'',1200,'EUR');
  IF NOT v_capture THEN RAISE EXCEPTION 'synthetic expired capture failed'; END IF;
  v_capture:=finance.apply_payabl_notification(
    v_team_payment,md5(v_team_payment::text)||md5(v_team_payment::text||'nonce'),
    '300106','capture',0,1794260000,
    'LFC'||left(replace(v_team_payment::text,'-',''),27),'',1500,'EUR');
  IF NOT v_capture THEN RAISE EXCEPTION 'synthetic first-team capture failed'; END IF;
  v_capture:=finance.apply_payabl_notification(
    v_cross_payment,md5(v_cross_payment::text)||md5(v_cross_payment::text||'nonce'),
    '300107','capture',0,1794260000,
    'LFC'||left(replace(v_cross_payment::text,'-',''),27),'',1500,'EUR');
  IF NOT v_capture THEN RAISE EXCEPTION 'synthetic cross-owner capture failed'; END IF;

  INSERT INTO hub.fan_subscriptions
    (id,subscriber_identity_id,organization_id,status,
     audience_parent,audience_player,provider,provider_reference,
     verified_at,expires_at,cancelled_at,consent_at)
  VALUES
    (gen_random_uuid(),v_good,'lefteria_fc_academy','active',
      true,false,'payabl','300101',clock_timestamp()-interval '1 day',
      clock_timestamp()+interval '25 days',NULL,clock_timestamp()-interval '1 day'),
    (gen_random_uuid(),v_refund,'lefteria_fc_academy','active',
      true,false,'payabl','300102',clock_timestamp()-interval '1 day',
      clock_timestamp()+interval '25 days',NULL,clock_timestamp()-interval '1 day'),
    (gen_random_uuid(),v_fake,'lefteria_fc_academy','active',
      true,false,'payabl','not-captured',clock_timestamp()-interval '1 day',
      clock_timestamp()+interval '25 days',NULL,clock_timestamp()-interval '1 day'),
    (gen_random_uuid(),v_wrong,'lefteria_fc_academy','active',
      true,false,'payabl','300103',clock_timestamp()-interval '1 day',
      clock_timestamp()+interval '25 days',NULL,clock_timestamp()-interval '1 day'),
    (gen_random_uuid(),v_inactive,'lefteria_fc_academy','active',
      true,false,'payabl','300104',clock_timestamp()-interval '1 day',
      clock_timestamp()+interval '25 days',NULL,clock_timestamp()-interval '1 day'),
    (gen_random_uuid(),v_old,'lefteria_fc_academy','active',
      true,false,'payabl','300105',clock_timestamp()-interval '45 days',
      clock_timestamp()-interval '10 days',NULL,clock_timestamp()-interval '46 days'),
    (gen_random_uuid(),v_pending,'lefteria_fc_academy','pending',
      true,false,'payabl',NULL,NULL,NULL,NULL,clock_timestamp()),
    (gen_random_uuid(),v_past,'lefteria_fc_academy','past_due',
      true,false,'payabl',NULL,NULL,NULL,NULL,clock_timestamp()),
    (gen_random_uuid(),v_cancelled,'lefteria_fc_academy','cancelled',
      true,false,'payabl',NULL,NULL,NULL,clock_timestamp(),clock_timestamp()),
    (gen_random_uuid(),v_team,'lefteria_fc','active',
      false,true,'payabl','300106',clock_timestamp()-interval '1 day',
      clock_timestamp()+interval '25 days',NULL,clock_timestamp()-interval '1 day'),
    (gen_random_uuid(),v_cross,'lefteria_fc','active',
      false,true,'payabl','300107',clock_timestamp()-interval '1 day',
      clock_timestamp()+interval '25 days',NULL,clock_timestamp()-interval '1 day');

  INSERT INTO hub.fan_checkout_links(subscription_id,payment_request_id,track)
  SELECT s.id,fixture.payment_id,fixture.track
  FROM (VALUES
    (v_good,v_good_payment,'parent'),(v_refund,v_refund_payment,'parent'),
    (v_fake,v_fake_payment,'parent'),(v_wrong,v_wrong_payment,'parent'),
    (v_inactive,v_inactive_payment,'parent'),(v_old,v_old_payment,'parent'),
    (v_team,v_team_payment,'player'),(v_cross,v_cross_payment,'player')
  ) AS fixture(identity_id,payment_id,track)
  JOIN hub.fan_subscriptions s ON s.subscriber_identity_id=fixture.identity_id;

  v_data:=hub.read_admin_fan_subscription_overview(v_token,'lefteria_fc_academy');
  IF v_data->>'organization_id'<>'lefteria_fc_academy'
    OR (v_data->'counts'->>'total_records')::integer<>9
    OR (v_data->'counts'->>'recorded_active')::integer<>6
    OR (v_data->'counts'->>'verified_active')::integer<>1
    OR (v_data->'counts'->>'pending')::integer<>1
    OR (v_data->'counts'->>'past_due')::integer<>1
    OR (v_data->'counts'->>'cancelled')::integer<>1
    OR (v_data->'counts'->>'expired')::integer<>0
    OR jsonb_array_length(v_data->'plans')<>2
    OR (v_data->'plans'->0->>'track')<>'both'
    OR (v_data->'plans'->1->>'track')<>'parent'
    OR (v_data->'plans'->0->>'is_enabled')::boolean
    OR NOT (v_data->'plans'->1->>'is_enabled')::boolean
    OR (SELECT array_agg(k ORDER BY k) FROM jsonb_object_keys(v_data) AS k)
      IS DISTINCT FROM ARRAY['as_of','counts','organization_id','plans']
  THEN RAISE EXCEPTION 'academy overview leaked, overstated or omitted verified scope: %',v_data; END IF;

  v_team_data:=hub.read_admin_fan_subscription_overview(v_token,'lefteria_fc');
  IF v_team_data->>'organization_id'<>'lefteria_fc'
    OR (v_team_data->'counts'->>'total_records')::integer<>2
    OR (v_team_data->'counts'->>'recorded_active')::integer<>2
    OR (v_team_data->'counts'->>'verified_active')::integer<>1
    OR jsonb_array_length(v_team_data->'plans')<>1
    OR v_team_data->'plans'->0->>'track'<>'player'
  THEN RAISE EXCEPTION 'first team overview leaked Academy ledger: %',v_team_data; END IF;

  -- Account and session policy is rechecked by the SQL function itself,
  -- not trusted from the browser's displayed role or selected organization.
  BEGIN
    PERFORM hub.read_admin_fan_subscription_overview(v_manager_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'delegated manager accessed organization-wide fan financial totals';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM hub.read_admin_fan_subscription_overview(v_other_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'ungranted Super Admin accessed Academy fan financial totals';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM hub.read_admin_fan_subscription_overview(repeat('0',64),'lefteria_fc_academy');
    RAISE EXCEPTION 'unknown admin session granted fan totals';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  UPDATE identity.admin_sessions SET revoked_at=clock_timestamp()
    WHERE token_digest=v_token;
  BEGIN
    PERFORM hub.read_admin_fan_subscription_overview(v_token,'lefteria_fc_academy');
    RAISE EXCEPTION 'revoked Super Admin session granted fan totals';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $hub_fan_verified_overview$;
ROLLBACK;
