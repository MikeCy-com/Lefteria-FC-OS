\set ON_ERROR_STOP on
-- Isolated migration-owner check only: no real customer data, no SMS/network,
-- and all synthetic entries are rolled back. Running as the app role is denied.
BEGIN;
DO $training_fee_tests$
DECLARE
  v_actor uuid:=gen_random_uuid();
  v_parent uuid:=gen_random_uuid();
  v_player text:='ci-fee-player-'||replace(gen_random_uuid()::text,'-','');
  v_fee uuid:=gen_random_uuid();
  v_second uuid:=gen_random_uuid();
  v_payment uuid:=gen_random_uuid();
  v_order text;
  v_count integer;
  v_status text;
  v_paid timestamptz;
  v_json jsonb;
  v_changed boolean;
  v_digest text:=repeat('d',64);
  v_cash_receipt uuid:=gen_random_uuid();
  v_cash_result jsonb;
BEGIN
  INSERT INTO identity.admin_accounts(id,username,display_name,role,is_active)
    VALUES(v_actor,'ci_fee_admin_'||left(replace(v_actor::text,'-',''),15),
      'Disposable fee test admin','manager',true);
  -- Synthetic manager with an isolated active auth session and Academy grant.
  UPDATE identity.admin_accounts
    SET password_hash='disposable_ci_hash_not_a_real_password'
    WHERE id=v_actor;
  INSERT INTO identity.admin_organization_grants(admin_account_id,organization_id)
    VALUES(v_actor,'lefteria_fc_academy');
  INSERT INTO identity.admin_sessions(
    id,token_digest,admin_account_id,credential_digest,created_at,expires_at,revoked_at)
    VALUES(gen_random_uuid(),v_digest,v_actor,repeat('a',64),
      clock_timestamp(),clock_timestamp()+interval '1 hour',NULL);
  INSERT INTO identity.mobile_identities(
    id,normalized_phone,role,display_name,is_active,platform,created_at)
    VALUES(v_parent,'35799123456','parent','Disposable parent',true,'mobile',
      clock_timestamp());
  INSERT INTO club.players(id,organization_id,name,number,position,team_type,
      team_id,primary_academy_group_id,image_url,is_active,is_archived,
      is_transferred,has_explicit_picker_fields)
    VALUES(v_player,'lefteria_fc_academy','Disposable academy child',9,
      'Midfielder','Academy',NULL,NULL,NULL,true,false,false,true);

  INSERT INTO finance.training_fee_issues(
    id,organization_id,player_id,payer_identity_id,fee_month,
    amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
  VALUES(v_fee,'lefteria_fc_academy',v_player,v_parent,DATE '2026-11-01',
    4500,'+35799123456','sandbox@example.invalid','Test','Parent',v_actor);
  IF (SELECT payer_phone FROM finance.training_fee_issues WHERE id=v_fee)
    <> '+35799123456' THEN
    RAISE EXCEPTION 'Invoice payer phone was not snapshot at issuance';
  END IF;
  -- A selected month is unique. Issuing October is NOT a twelve-month plan.
  BEGIN
    INSERT INTO finance.training_fee_issues(
      id,organization_id,player_id,payer_identity_id,fee_month,
      amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
    VALUES(gen_random_uuid(),'lefteria_fc_academy',v_player,v_parent,
      DATE '2026-11-01',4500,'+35799123456','sandbox@example.invalid','Test','Parent',v_actor);
    RAISE EXCEPTION 'Duplicate child/month accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  INSERT INTO finance.training_fee_issues(
    id,organization_id,player_id,payer_identity_id,fee_month,
    amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
  VALUES(v_second,'lefteria_fc_academy',v_player,v_parent,DATE '2026-12-01',
    4500,'+35799123456','sandbox@example.invalid','Test','Parent',v_actor);
  SELECT count(*) INTO v_count FROM finance.training_fee_issues
    WHERE player_id=v_player;
  IF v_count<>2 THEN
    RAISE EXCEPTION 'Training-fee issue generated unrequested months';
  END IF;

  -- Record full cash for December only. Verify a repeated request is
  -- idempotent and a different receipt cannot pay twice.
  v_cash_result:=finance.record_admin_training_fee_cash(
    v_digest,'lefteria_fc_academy',v_second,v_cash_receipt,'');
  IF v_cash_result->>'payment_method'<>'cash'
     OR v_cash_result->>'status'<>'paid'
     OR (v_cash_result->>'replay')::boolean THEN
    RAISE EXCEPTION 'Manager cash receipt was not recorded';
  END IF;
  v_cash_result:=finance.record_admin_training_fee_cash(
    v_digest,'lefteria_fc_academy',v_second,v_cash_receipt,'');
  IF NOT (v_cash_result->>'replay')::boolean THEN
    RAISE EXCEPTION 'Duplicate cash receipt created';
  END IF;
  BEGIN
    PERFORM finance.record_admin_training_fee_cash(
      v_digest,'lefteria_fc_academy',v_second,gen_random_uuid(),'');
    RAISE EXCEPTION 'Second cash receipt accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;
  IF (SELECT count(*) FROM finance.training_fee_cash_receipts
    WHERE fee_issue_id=v_second)<>1 OR
     (SELECT status FROM finance.training_fee_issues WHERE id=v_second)<>'paid' THEN
    RAISE EXCEPTION 'Cash did not settle December exactly once';
  END IF;
  v_order:='LFC'||left(replace(v_payment::text,'-',''),27);
  INSERT INTO finance.payment_requests(
    id,organization_id,actor_id,client_request_id,order_id,purpose,
    amount_cents,payer_email,payer_first_name,payer_last_name,callback_digest,
    status,checkout_url,initial_transaction_id)
  VALUES(v_payment,'lefteria_fc_academy',v_actor,gen_random_uuid(),v_order,
    'Academy training 2026-11',4500,'sandbox@example.invalid',
    'Test','Parent',repeat('e',64),'ready',
    'https://sandbox.payabl.com/pay/test','5000');
  UPDATE finance.training_fee_issues SET payment_request_id=v_payment WHERE id=v_fee;
  SELECT status,paid_at INTO v_status,v_paid FROM finance.training_fee_issues
    WHERE id=v_fee;
  IF v_status<>'issued' OR v_paid IS NOT NULL THEN
    RAISE EXCEPTION 'Checkout issuance falsely marked child fee paid';
  END IF;

  -- Merely being assigned as a payer does not authorize a PDF: the
  -- current canonical parent/child link must also exist at read time.
  BEGIN
    PERFORM finance.read_parent_invoice_pdf(v_parent,'35799123456','parent',
      clock_timestamp()+interval '1 hour',ARRAY[v_fee]);
    RAISE EXCEPTION 'Unlinked parent could read the private invoice PDF';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;

  -- The unprivileged role cannot see raw PDF data or directly mark cash paid
  -- (verified by separate finance role grants and segregation probe).
  -- The only authorized paid transition is a provider capture applied to
  -- this one linked payment, never the opened return URL.
  v_changed:=finance.apply_payabl_notification(v_payment,repeat('e',64),
    '5001','capture',0,1794260000,v_order,'',4500,'EUR');
  SELECT status,paid_at INTO v_status,v_paid FROM finance.training_fee_issues
    WHERE id=v_fee;
  IF NOT v_changed OR v_status<>'paid' OR v_paid IS NULL THEN
    RAISE EXCEPTION 'Gateway capture did not settle exact child/month';
  END IF;
  SELECT status INTO v_status FROM finance.training_fee_issues WHERE id=v_second;
  IF v_status<>'paid'
    OR (SELECT count(*) FROM finance.training_fee_cash_receipts WHERE fee_issue_id=v_second)<>1 THEN
    RAISE EXCEPTION 'Card capture changed independently paid cash invoice';
  END IF;

  -- Parent may request a day (including 31) and express consent, but the
  -- system cannot activate automatic collection before provider approval.
  v_json:=finance.set_parent_fee_preferences(v_parent,'35799123456',
    'parent',clock_timestamp()+interval '1 hour',31,true,true);
  IF v_json->>'preferred_day'<>'31'
     OR (v_json->>'automatic_collection_active')::boolean
     OR (SELECT automatic_collection_enabled FROM finance.parent_fee_preferences
          WHERE parent_identity_id=v_parent) THEN
    RAISE EXCEPTION 'Unapproved automatic recurring payment activated';
  END IF;
  -- Issued due dates are immutable snapshots. A preferred day of 31
  -- clamps to February's last day for a NEW invoice and never mutates
  -- November's previously issued PDF.
  IF (SELECT invoice_due_date FROM finance.training_fee_issues
      WHERE id=v_fee)<>DATE '2026-11-05' THEN
    RAISE EXCEPTION 'Changing preference rewrote issued invoice due date';
  END IF;
  DECLARE
    v_feb uuid := gen_random_uuid();
  BEGIN
    INSERT INTO finance.training_fee_issues(
      id,organization_id,player_id,payer_identity_id,fee_month,
      amount_cents,payer_phone,payer_email,payer_first_name,payer_last_name,issued_by)
    VALUES(v_feb,'lefteria_fc_academy',v_player,v_parent,DATE '2027-02-01',
      4500,'+35799123456','sandbox@example.invalid','Test','Parent',v_actor);
    IF (SELECT invoice_due_date FROM finance.training_fee_issues
      WHERE id=v_feb)<>DATE '2027-02-28' THEN
      RAISE EXCEPTION 'February due date did not clamp to month end';
    END IF;
    DELETE FROM finance.training_fee_issues WHERE id=v_feb;
  END;
  -- Saving preferences or issuing a payment for November did not create
  -- unseen installments for January–October.
  SELECT count(*) INTO v_count FROM finance.training_fee_issues WHERE player_id=v_player;
  IF v_count<>2 THEN
    RAISE EXCEPTION 'Preferences unexpectedly generated charges';
  END IF;
END $training_fee_tests$;
ROLLBACK;
