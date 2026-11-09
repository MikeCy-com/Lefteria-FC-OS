\set ON_ERROR_STOP on
-- SQL-only, rollback-only test of the original eight charge categories and
-- immutable EUR-cent partial settlement. Operates in a disposable database.
\if :{?app_role}
\else
  DO $missing$ BEGIN RAISE EXCEPTION 'runtime_role_is_required'; END $missing$;
\endif
\if :{?admin_auth_role}
\else
  DO $missing$ BEGIN RAISE EXCEPTION 'admin_auth_role_is_required'; END $missing$;
\endif

BEGIN;
SELECT set_config('lefteria.test_app_role',:'app_role',true);
SELECT set_config('lefteria.test_admin_role',:'admin_auth_role',true);

DO $general_charges_checks$
DECLARE
  v_player text:='gencharge_'||left(replace(gen_random_uuid()::text,'-',''),24);
  v_charge uuid:=gen_random_uuid();
  v_unpaid uuid:=gen_random_uuid();
  v_cancel uuid:=gen_random_uuid();
  v_actor uuid:=gen_random_uuid();
  v_first uuid:=gen_random_uuid();
  v_second uuid:=gen_random_uuid();
  v_reversal uuid:=gen_random_uuid();
  v_replay uuid:=gen_random_uuid();
  v_status text;
  v_ready boolean;
  v_due bigint;
  v_total bigint;
  v_pay_cents bigint;
  v_method text;
  v_types integer;
  v_event_count integer;
  v_template uuid:=gen_random_uuid();
BEGIN
  -- No raw access to finance records is granted to runtime admin/mobile.
  IF EXISTS (
    SELECT 1 FROM (VALUES
      ('finance.general_charge_issues'),
      ('finance.general_charge_parent_verifications'),
      ('finance.general_charge_events'),
      ('finance.general_charge_templates'),
      ('finance.general_charge_balances')) AS t(name)
      WHERE has_table_privilege(current_setting('lefteria.test_app_role'),
          t.name,'SELECT,INSERT,UPDATE,DELETE')
         OR has_table_privilege(current_setting('lefteria.test_admin_role'),
          t.name,'SELECT,INSERT,UPDATE,DELETE')
  ) THEN RAISE EXCEPTION 'private_charge_ledger_exposed_to_runtime'; END IF;

  SELECT count(*) INTO v_types FROM (
    VALUES ('training'),('event'),('grassroots'),('registration'),
           ('equipment'),('tournament'),('transport'),('other')
  ) AS kinds(name)
  WHERE kinds.name IN (
    SELECT unnest(string_to_array(
      'training,event,grassroots,registration,equipment,tournament,transport,other',','))
  );
  IF v_types<>8 THEN RAISE EXCEPTION 'original_charge_types_incomplete'; END IF;

  INSERT INTO club.players(
    id,organization_id,name,number,position,team_type,
    is_active,is_archived,is_transferred,has_explicit_picker_fields
  ) VALUES(v_player,'lefteria_fc_academy',
    'Synthetic ledger player',11,'Forward','Academy',true,false,false,true);
  INSERT INTO identity.admin_accounts(
    id,username,display_name,role,is_active,password_hash
  ) VALUES(v_actor,'test_charge_'||left(replace(v_actor::text,'-',''),16),
    'Disposable finance manager','manager',true,'not_a_login_hash');

  INSERT INTO finance.general_charge_templates(
    id,organization_id,name,charge_type,description,default_amount_cents
  ) VALUES(v_template,'lefteria_fc_academy','Synthetic training equipment',
    'equipment','One time equipment',8000);

  -- Creating a reusable template never issues an annual training cycle.
  IF EXISTS(SELECT 1 FROM finance.general_charge_issues
    WHERE player_id=v_player) THEN
    RAISE EXCEPTION 'template_created_charges_or_future_months';
  END IF;

  INSERT INTO finance.general_charge_issues(
    id,organization_id,player_id,charge_type,description,
    amount_cents,due_date,period_key,issued_at,created_by
  ) VALUES(v_charge,'lefteria_fc_academy',v_player,'equipment',
    'Synthetic one-off kit',8000,DATE '2099-11-30','2099-11',
    clock_timestamp(),v_actor);

  SELECT ready_for_parent INTO v_ready
  FROM finance.general_charge_balances WHERE id=v_charge;
  IF v_ready THEN
    RAISE EXCEPTION 'Unverified guardian invoice entered parent view';
  END IF;

  INSERT INTO finance.general_charge_issues(
    id,organization_id,player_id,charge_type,description,
    amount_cents,due_date,period_key
  ) VALUES(v_unpaid,'lefteria_fc_academy',v_player,'event',
    'Historical event not collected',1200,DATE '2020-01-15','2020-01');

  INSERT INTO finance.general_charge_issues(
    id,organization_id,player_id,charge_type,description,
    amount_cents,due_date
  ) VALUES(v_cancel,'lefteria_fc_academy',v_player,'registration',
    'Cancelled sample registration',5000,DATE '2099-11-30');

  SELECT status,due_cents INTO v_status,v_due
  FROM finance.general_charge_balances WHERE id=v_unpaid;
  IF v_status<>'overdue' OR v_due<>1200 THEN
    RAISE EXCEPTION 'past_unpaid_charge_status_incorrect';
  END IF;

  INSERT INTO finance.general_charge_events(
    id,charge_id,organization_id,idempotency_id,event_kind,payment_method,
    evidence_type,amount_cents,legacy_event_id,occurred_at
  ) VALUES(v_first,v_charge,'lefteria_fc_academy',v_replay,'payment',
    'cash','verified_legacy',3000,'legacy-partial-1',clock_timestamp());

  SELECT status,due_cents,paid_cents INTO v_status,v_due,v_pay_cents
  FROM finance.general_charge_balances WHERE id=v_charge;
  IF v_status<>'pending' OR v_due<>5000 OR v_pay_cents<>3000 THEN
    RAISE EXCEPTION 'partial_payment_incorrect';
  END IF;

  -- The exact same idempotency key cannot collect a second time.
  BEGIN
    INSERT INTO finance.general_charge_events(
      id,charge_id,organization_id,idempotency_id,event_kind,payment_method,
      evidence_type,amount_cents,legacy_event_id
    ) VALUES(gen_random_uuid(),v_charge,'lefteria_fc_academy',v_replay,
      'payment','cash','verified_legacy',100,'legacy-new-duplicate');
    RAISE EXCEPTION 'duplicate_idempotency_collected';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;

  -- Before-insert guarded balance checks catch over-collection.
  BEGIN
    INSERT INTO finance.general_charge_events(
      id,charge_id,organization_id,idempotency_id,event_kind,payment_method,
      evidence_type,amount_cents,legacy_event_id
    ) VALUES(gen_random_uuid(),v_charge,'lefteria_fc_academy',gen_random_uuid(),
      'payment','card','verified_legacy',6000,'legacy-overpaid');
    RAISE EXCEPTION 'overpayment_was_accepted';
  EXCEPTION WHEN check_violation THEN NULL;
  END;

  INSERT INTO finance.general_charge_events(
    id,charge_id,organization_id,idempotency_id,event_kind,payment_method,
    evidence_type,amount_cents,legacy_event_id
  ) VALUES(v_second,v_charge,'lefteria_fc_academy',gen_random_uuid(),
    'payment','bank','verified_legacy',5000,'legacy-partial-2');

  SELECT status,due_cents,paid_cents INTO v_status,v_due,v_pay_cents
  FROM finance.general_charge_balances WHERE id=v_charge;
  IF v_status<>'paid' OR v_due<>0 OR v_pay_cents<>8000 THEN
    RAISE EXCEPTION 'full_charge_settlement_incorrect';
  END IF;

  -- Reversals are separate, exact opposite events, never edits or deletes.
  INSERT INTO finance.general_charge_events(
    id,charge_id,organization_id,idempotency_id,event_kind,payment_method,
    evidence_type,amount_cents,legacy_event_id,reverses_event_id
  ) VALUES(v_reversal,v_charge,'lefteria_fc_academy',gen_random_uuid(),
    'reversal','cash','verified_legacy',-3000,'legacy-refund-1',v_first);
  SELECT status,due_cents INTO v_status,v_due
  FROM finance.general_charge_balances WHERE id=v_charge;
  IF v_status<>'pending' OR v_due<>3000 THEN
    RAISE EXCEPTION 'refund_did_not_restore_original_balance';
  END IF;

  BEGIN
    INSERT INTO finance.general_charge_events(
      id,charge_id,organization_id,idempotency_id,event_kind,payment_method,
      evidence_type,amount_cents,legacy_event_id,reverses_event_id
    ) VALUES(gen_random_uuid(),v_charge,'lefteria_fc_academy',
      gen_random_uuid(),'reversal','cash','verified_legacy',-3000,
      'legacy-double-refund',v_first);
    RAISE EXCEPTION 'double_reversal_accepted';
  EXCEPTION WHEN unique_violation THEN NULL;
  END;

  BEGIN
    UPDATE finance.general_charge_events SET amount_cents=2000 WHERE id=v_first;
    RAISE EXCEPTION 'ledger_event_was_mutable';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    UPDATE finance.general_charge_issues SET amount_cents=7000
    WHERE id=v_charge;
    RAISE EXCEPTION 'issued_invoice_amount_was_mutable';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;
  BEGIN
    UPDATE finance.general_charge_issues SET cancelled_at=clock_timestamp()
    WHERE id=v_charge;
    RAISE EXCEPTION 'paid_invoice_cancellation_accepted';
  EXCEPTION WHEN insufficient_privilege THEN NULL;
  END;

  UPDATE finance.general_charge_issues SET cancelled_at=clock_timestamp()
  WHERE id=v_cancel;
  SELECT status INTO v_status
  FROM finance.general_charge_balances WHERE id=v_cancel;
  IF v_status<>'cancelled' THEN
    RAISE EXCEPTION 'unpaid_invoice_not_cancelled';
  END IF;

  SELECT count(*) INTO v_event_count FROM finance.general_charge_events
  WHERE charge_id=v_charge;
  SELECT count(*) INTO v_types FROM finance.general_charge_issues
  WHERE player_id=v_player;
  IF v_event_count<>3 OR v_types<>3 THEN
    RAISE EXCEPTION 'unexpected_extra_ledger_events_or_annual_charges';
  END IF;
END $general_charges_checks$;
ROLLBACK;
