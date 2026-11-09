\set ON_ERROR_STOP on

-- Disposable CI-only state transition verification. Run as the migrations
-- owner in the isolated identity test DB. All synthetic rows are rolled back.
BEGIN;
DO $payabl_callback_checks$
DECLARE
  v_owner text := 'lefteria_fc';
  v_card uuid := gen_random_uuid();
  v_bank uuid := gen_random_uuid();
  v_mismatch uuid := gen_random_uuid();
  v_order_card text;
  v_order_bank text;
  v_order_mismatch text;
  v_state text;
  v_transaction text;
  v_changed boolean;
  v_stamp bigint := 1794260000;
BEGIN
  IF NOT EXISTS (SELECT 1 FROM club.organizations WHERE id=v_owner) THEN
    RAISE EXCEPTION 'Disposable fixture must seed the club organization';
  END IF;
  v_order_card := 'LFC'||replace(v_card::text,'-','');
  v_order_bank := 'LFC'||replace(v_bank::text,'-','');
  v_order_mismatch := 'LFC'||replace(v_mismatch::text,'-','');
  INSERT INTO finance.payment_requests (
    id,organization_id,actor_id,client_request_id,order_id,purpose,
    amount_cents,currency,payer_email,payer_first_name,payer_last_name,
    callback_digest,status,initial_transaction_id)
  VALUES
    (v_card,v_owner,gen_random_uuid(),gen_random_uuid(),v_order_card,
      'Synthetic card payment',1500,'EUR','test@example.invalid','Test','Card',
      repeat('a',64),'ready','1000'),
    (v_bank,v_owner,gen_random_uuid(),gen_random_uuid(),v_order_bank,
      'Synthetic bank payment',1500,'EUR','test@example.invalid','Test','Bank',
      repeat('b',64),'ready','1001'),
    (v_mismatch,v_owner,gen_random_uuid(),gen_random_uuid(),v_order_mismatch,
      'Synthetic amount mismatch',1500,'EUR','test@example.invalid','Test','Mismatch',
      repeat('c',64),'ready','1002');

  -- Provider docs: capture/0/not_credited_yet means acceptance, NOT
  -- settlement. A later 'credited' claim is UNSIGNED by simplified SHA256:
  -- it requires reconciliation, not an automatic paid state.
  v_changed := finance.apply_payabl_notification(
    v_bank,repeat('b',64),'2001','capture',0,v_stamp,v_order_bank,
    'not_credited_yet',1500,'EUR');
  SELECT status INTO v_state FROM finance.payment_requests WHERE id=v_bank;
  IF v_changed OR v_state<>'awaiting_settlement' THEN
    RAISE EXCEPTION 'Prematurely marked accepted bank payment as paid';
  END IF;
  v_changed := finance.apply_payabl_notification(
    v_bank,repeat('b',64),'2001','capture',0,v_stamp+1,v_order_bank,
    'credited',1500,'EUR');
  SELECT status INTO v_state FROM finance.payment_requests WHERE id=v_bank;
  IF v_changed OR v_state<>'review_required' THEN
    RAISE EXCEPTION 'Unsigned credited state was accepted as proof';
  END IF;

  -- A properly correlated card-only callback without a separate APM state
  -- may succeed. Callback IDs need not equal init transaction IDs.
  v_changed := finance.apply_payabl_notification(
    v_card,repeat('a',64),'2999','capture',0,v_stamp,v_order_card,
    '',1500,'EUR');
  SELECT status,captured_transaction_id INTO v_state,v_transaction
    FROM finance.payment_requests WHERE id=v_card;
  IF NOT v_changed OR v_state<>'paid' OR v_transaction<>'2999' THEN
    RAISE EXCEPTION 'Verified card capture did not settle correctly';
  END IF;
  v_changed := finance.apply_payabl_notification(
    v_card,repeat('a',64),'2999','capture',0,v_stamp,v_order_card,
    '',1500,'EUR');
  IF v_changed OR
    (SELECT count(*) FROM finance.payment_notifications WHERE request_id=v_card)<>1 THEN
    RAISE EXCEPTION 'Callback replay duplicated the capture';
  END IF;
  v_changed := finance.apply_payabl_notification(
    v_card,repeat('a',64),'2999','refund',0,v_stamp+3,v_order_card,
    'refunded',1500,'EUR');
  SELECT status INTO v_state FROM finance.payment_requests WHERE id=v_card;
  IF v_changed OR v_state<>'paid' THEN
    RAISE EXCEPTION 'Unexpected refund silently changed paid state';
  END IF;

  -- An unexpected amount must lock the request into manual review, even
  -- if a later nominally successful callback arrives.
  v_changed := finance.apply_payabl_notification(
    v_mismatch,repeat('c',64),'3003','capture',0,v_stamp,v_order_mismatch,
    '',999,'EUR');
  SELECT status INTO v_state FROM finance.payment_requests WHERE id=v_mismatch;
  IF v_changed OR v_state<>'review_required' THEN
    RAISE EXCEPTION 'Wrong amount incorrectly marked paid';
  END IF;
  v_changed := finance.apply_payabl_notification(
    v_mismatch,repeat('c',64),'3004','capture',0,v_stamp+1,v_order_mismatch,
    '',1500,'EUR');
  SELECT status INTO v_state FROM finance.payment_requests WHERE id=v_mismatch;
  IF v_changed OR v_state<>'review_required' THEN
    RAISE EXCEPTION 'Previously mismatched amount upgraded without reconciliation';
  END IF;

  -- A valid callback nonce cannot be used on a different order.
  BEGIN
    PERFORM finance.apply_payabl_notification(
      v_card,repeat('a',64),'4004','capture',0,v_stamp+10,v_order_bank,
      '',1500,'EUR');
    RAISE EXCEPTION 'Cross-order callback unexpectedly accepted';
  EXCEPTION
    WHEN insufficient_privilege THEN NULL;
  END;
  SELECT status INTO v_state FROM finance.payment_requests WHERE id=v_bank;
  IF v_state<>'review_required' THEN
    RAISE EXCEPTION 'Cross-order callback touched another payment';
  END IF;
END $payabl_callback_checks$;
ROLLBACK;
