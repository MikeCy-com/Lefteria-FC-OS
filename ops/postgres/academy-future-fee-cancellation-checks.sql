\set ON_ERROR_STOP on
-- Disposable, synthetic PostgreSQL-only proof. Never touches live payments.
BEGIN;
DO $future_fee_checks$
DECLARE
  v_super uuid:=gen_random_uuid();
  v_manager uuid:=gen_random_uuid();
  v_parent uuid:=gen_random_uuid();
  v_player text:='ci-cancel-'||md5(gen_random_uuid()::text);
  v_fee uuid:=gen_random_uuid();
  v_paid_fee uuid:=gen_random_uuid();
  v_linked_fee uuid:=gen_random_uuid();
  v_linked_request uuid:=gen_random_uuid();
  v_request uuid:=gen_random_uuid();
  v_month date:=(date_trunc('month',
    clock_timestamp() AT TIME ZONE 'Asia/Nicosia')+interval '1 month')::date;
  v_next_month date:=(date_trunc('month',
    clock_timestamp() AT TIME ZONE 'Asia/Nicosia')+interval '2 months')::date;
  v_super_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_manager_digest text:=md5(gen_random_uuid()::text)||md5(gen_random_uuid()::text);
  v_json jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active,
    password_hash,invite_pending,otp2fa_enabled) VALUES
    (v_super,'ci-cancel-super-'||substr(md5(v_super::text),1,8),
      'CI Future Fee Super','super_admin',true,'ci-fixture-hash',false,false),
    (v_manager,'ci-cancel-manager-'||substr(md5(v_manager::text),1,8),
      'CI Future Fee Manager','manager',true,'ci-fixture-hash',false,false);
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(v_super,'lefteria_fc_academy'),(v_manager,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(id,token_digest,admin_account_id,
    credential_digest,created_at,expires_at) VALUES
    (gen_random_uuid(),v_super_digest,v_super,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour'),
    (gen_random_uuid(),v_manager_digest,v_manager,repeat('b',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour');
  INSERT INTO identity.mobile_identities(id,normalized_phone,role,display_name,
    is_active,platform,created_at)
    VALUES(v_parent,'35799123456','parent','CI Future Fee Guardian',
      true,'mobile',clock_timestamp());
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
    VALUES(v_player,'lefteria_fc_academy','CI Future Fee Child',1,'Forward',
      'Academy',true,false,false,true);

  INSERT INTO finance.training_fee_issues(id,organization_id,player_id,
    payer_identity_id,fee_month,amount_cents,payer_phone,payer_email,
    payer_first_name,payer_last_name,issued_by) VALUES
    (v_fee,'lefteria_fc_academy',v_player,v_parent,v_month,
     6000,'+35799123456','ci@example.invalid','CI','Guardian',v_super),
    (v_paid_fee,'lefteria_fc_academy',v_player,v_parent,v_next_month,
     5000,'+35799123456','ci@example.invalid','CI','Guardian',v_super);
  -- A manager cannot cancel global Academy invoices, even with org grant.
  BEGIN
    PERFORM finance.cancel_admin_future_training_fee(v_manager_digest,
      'lefteria_fc_academy',v_fee,gen_random_uuid(),'Player has left the academy');
    RAISE EXCEPTION 'site_manager_cancelled_global_fee';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  v_json:=finance.cancel_admin_future_training_fee(v_super_digest,
    'lefteria_fc_academy',v_fee,v_request,'Player has left the academy');
  IF v_json->>'status'<>'cancelled' OR (v_json->>'replay')::boolean
    OR (SELECT cancelled_at IS NULL OR cancel_reason IS NULL
       FROM finance.training_fee_issues WHERE id=v_fee)
  THEN RAISE EXCEPTION 'future_fee_cancellation_not_committed'; END IF;
  v_json:=finance.cancel_admin_future_training_fee(v_super_digest,
    'lefteria_fc_academy',v_fee,v_request,'Player has left the academy');
  IF NOT (v_json->>'replay')::boolean THEN
    RAISE EXCEPTION 'identical_retry_not_idempotent'; END IF;
  BEGIN
    PERFORM finance.cancel_admin_future_training_fee(v_super_digest,
      'lefteria_fc_academy',v_fee,gen_random_uuid(),'Different request');
    RAISE EXCEPTION 'changed_cancellation_attempt_was_accepted';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  IF (SELECT count(*) FROM audit.security_events
      WHERE actor_identity_id=v_super AND
        action='admin.finance.training_fee.cancel_future')<>1
  THEN RAISE EXCEPTION 'cancellation_audit_not_exactly_once'; END IF;

  v_json:=finance.read_admin_accounting_overview(
    v_super_digest,'lefteria_fc_academy',v_month);
  IF (v_json#>>'{academy_training_fees,issued_count}')::int<>0
     OR (v_json#>>'{academy_training_fees,issued_cents}')::bigint<>0
     OR (v_json#>>'{academy_training_fees,open_cents}')::bigint<>0
     OR (v_json#>>'{academy_training_fees,paid_cents}')::bigint<>0
     OR (v_json#>>'{academy_training_fees,cancelled_count}')::int<>1
     OR (v_json#>>'{academy_training_fees,cancelled_cents}')::bigint<>6000
  THEN RAISE EXCEPTION 'cancelled_future_fee_pollutes_accounting'; END IF;
  v_json:=finance.read_admin_training_fees(v_super_digest,'lefteria_fc_academy');
  IF NOT EXISTS (
    SELECT 1 FROM jsonb_array_elements(v_json->'fees') f
    WHERE f->>'id'=v_fee::text AND f->>'status'='cancelled'
      AND f->>'cancel_reason'='Player has left the academy'
  ) THEN RAISE EXCEPTION 'cancelled_fee_missing_from_admin_audit_history'; END IF;
  BEGIN
    PERFORM finance.read_admin_invoice_pdf(v_super_digest,
      'lefteria_fc_academy',ARRAY[v_fee]);
    RAISE EXCEPTION 'cancelled_fee_printed_as_unpaid_invoice';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;

  -- Paid cash fees are immutable, and no cancellation alters revenue.
  v_json:=finance.record_admin_training_fee_cash(v_super_digest,
    'lefteria_fc_academy',v_paid_fee,gen_random_uuid(),'');
  BEGIN
    PERFORM finance.cancel_admin_future_training_fee(v_super_digest,
      'lefteria_fc_academy',v_paid_fee,gen_random_uuid(),'Player has left the academy');
    RAISE EXCEPTION 'paid_cash_fee_cancelled';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  v_json:=finance.read_admin_accounting_overview(
    v_super_digest,'lefteria_fc_academy',v_next_month);
  IF (v_json#>>'{academy_training_fees,paid_cents}')::bigint<>5000
     OR (v_json#>>'{academy_training_fees,cancelled_count}')::int<>0
  THEN RAISE EXCEPTION 'cash_revenue_changed_by_future_cancellation'; END IF;
  IF (SELECT count(*) FROM finance.training_fee_cash_receipts
      WHERE fee_issue_id=v_paid_fee)<>1
  THEN RAISE EXCEPTION 'paid_cash_receipt_disappeared'; END IF;

  -- A provider-linked fee must be reviewed outside this cancellation path.
  INSERT INTO finance.payment_requests(id,organization_id,actor_id,
    client_request_id,order_id,purpose,amount_cents,payer_email,
    payer_first_name,payer_last_name,callback_digest,status)
  VALUES(v_linked_request,'lefteria_fc_academy',v_super,gen_random_uuid(),
    'LFC'||left(replace(v_linked_request::text,'-',''),27),
    'Academy training '||to_char(v_month,'YYYY-MM'),3000,'ci@example.invalid',
    'CI','Guardian',repeat('e',64),'initiating');
  INSERT INTO finance.training_fee_issues(id,organization_id,player_id,
    payer_identity_id,fee_month,amount_cents,payer_phone,payer_email,
    payer_first_name,payer_last_name,issued_by,payment_request_id)
  VALUES(v_linked_fee,'lefteria_fc_academy',v_player,v_parent,
    (v_month+interval '2 months')::date,3000,
    '+35799123456','ci@example.invalid','CI','Guardian',v_super,v_linked_request);
  BEGIN
    PERFORM finance.cancel_admin_future_training_fee(v_super_digest,
      'lefteria_fc_academy',v_linked_fee,gen_random_uuid(),'Player has left the academy');
    RAISE EXCEPTION 'provider_linked_fee_cancelled_without_reconciliation';
  EXCEPTION WHEN unique_violation THEN NULL; END;
  -- The complete per-player review must not be restricted to the global
  -- recent-100 list. Add later synthetic fees for another player.
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields)
    VALUES(v_player||'-other','lefteria_fc_academy','CI Other Child',2,'Forward',
      'Academy',true,false,false,true);
  INSERT INTO finance.training_fee_issues(id,organization_id,player_id,
    payer_identity_id,fee_month,amount_cents,payer_phone,payer_email,
    payer_first_name,payer_last_name,issued_by)
  SELECT gen_random_uuid(),'lefteria_fc_academy',v_player||'-other',v_parent,
    (v_next_month+(n||' months')::interval)::date,6000,'+35799123456',
    'ci@example.invalid','CI','Guardian',v_super
  FROM generate_series(3,103) n;
  INSERT INTO finance.training_fee_issues(id,organization_id,player_id,
    payer_identity_id,fee_month,amount_cents,payer_phone,payer_email,
    payer_first_name,payer_last_name,issued_by)
  VALUES(gen_random_uuid(),'lefteria_fc_academy',v_player,v_parent,
    (v_month+interval '3 months')::date,4000,'+35799123456',
    'ci@example.invalid','CI','Guardian',v_super);

  v_json:=finance.read_admin_training_fees(v_super_digest,'lefteria_fc_academy');
  IF EXISTS(SELECT 1 FROM jsonb_array_elements(v_json->'fees') f
    WHERE f->>'id'=v_fee::text)
  THEN RAISE EXCEPTION 'global list test did not exceed the old cap'; END IF;
  v_json:=finance.read_admin_player_training_fees(v_super_digest,'lefteria_fc_academy',v_player);
  IF v_json->>'player_id' IS DISTINCT FROM v_player
    OR v_json->>'player_name' IS DISTINCT FROM 'CI Future Fee Child'
    OR v_json->>'player_active' IS DISTINCT FROM 'true'
    OR v_json#>>'{lifecycle_review,is_active}' IS DISTINCT FROM 'true'
    OR v_json#>>'{lifecycle_review,is_archived}' IS DISTINCT FROM 'false'
    OR v_json#>>'{lifecycle_review,is_transferred}' IS DISTINCT FROM 'false'
    OR v_json#>>'{lifecycle_review,group_memberships}' IS DISTINCT FROM '0'
    OR v_json#>>'{lifecycle_review,recorded_guardian_bindings}' IS DISTINCT FROM '0'
    OR v_json#>>'{lifecycle_review,unrevoked_guardian_permissions}' IS DISTINCT FROM '0'
    OR jsonb_array_length(v_json->'fees')<>4
    OR EXISTS(SELECT 1 FROM jsonb_array_elements(v_json->'fees') f
      WHERE f->>'player_id' IS DISTINCT FROM v_player)
    OR (SELECT count(*) FROM jsonb_array_elements(v_json->'fees') f
      WHERE f->>'cancel_eligible'='true')<>1
    OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_json->'fees') f
      WHERE f->>'id'=v_fee::text AND f->>'status'='cancelled' AND f->>'cancel_eligible'='false')
    OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_json->'fees') f
      WHERE f->>'id'=v_paid_fee::text AND f->>'status'='paid' AND f->>'cancel_eligible'='false')
    OR NOT EXISTS(SELECT 1 FROM jsonb_array_elements(v_json->'fees') f
      WHERE f->>'id'=v_linked_fee::text AND f->>'cancel_eligible'='false')
  THEN RAISE EXCEPTION 'exact player review omitted history or classified cancellation incorrectly'; END IF;
  BEGIN
    PERFORM finance.read_admin_player_training_fees(v_manager_digest,'lefteria_fc_academy',v_player);
    RAISE EXCEPTION 'manager read restricted player finance history';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  BEGIN
    PERFORM finance.read_admin_player_training_fees(v_super_digest,'lefteria_fc_academy','missing-player');
    RAISE EXCEPTION 'missing player finance review accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
  UPDATE club.players SET is_active=false,is_archived=true WHERE id=v_player;
  v_json:=finance.read_admin_player_training_fees(v_super_digest,'lefteria_fc_academy',v_player);
  IF v_json->>'player_active' IS DISTINCT FROM 'false' OR jsonb_array_length(v_json->'fees')<>4
    OR v_json#>>'{lifecycle_review,is_active}' IS DISTINCT FROM 'false'
    OR v_json#>>'{lifecycle_review,is_archived}' IS DISTINCT FROM 'true'
  THEN RAISE EXCEPTION 'inactive player lost finance history'; END IF;
END $future_fee_checks$;
ROLLBACK;
