import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115953000_PosCashierAtomicReceiptAuthority.cs');
const accepted=read('ops/postgres/pos-site-isolation-checks.sql');
const ledger=read('ops/postgres/pos-cashier-ledger-checks.sql');
const original=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110152000_PosVerifiedReceiptIssuer.cs');
const tender=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110157000_PosVerifiedCashTender.cs');

test('receipt issuer now permits only named assigned Cashier who owns exact sale and active shift',()=>{
  assert.match(original,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION pos_cashier.issue_verified_internal_receipt/);
  assert.match(migration,/v_role NOT IN \('manager','super_admin','cashier'\)/);
  assert.match(migration,/IF v_role='cashier' THEN/);
  assert.match(migration,/v_sale.cashier_id<>v_actor/);
  assert.match(migration,/pos_cashier.current_cashier_authority\(/);
  assert.match(migration,/sh.register_id=v_sale.register_id/);
  assert.match(migration,/sh.cashier_id=v_actor AND sh.closed_at IS NULL/);
  assert.match(migration,/receipt_cashier_shift_closed/);
  assert.match(migration,/receipt_cashier_scope_changed/);
  assert.match(migration,/REVOKE ALL ON FUNCTION pos_cashier.issue_verified_internal_receipt/);
  assert.doesNotMatch(migration,/GRANT EXECUTE|GRANT SELECT|GRANT INSERT|GRANT UPDATE/);
});

test('cash payment, immutable receipt and physical stock deduct inside one SQL transaction',()=>{
  assert.match(tender,/v_receipt:=pos_cashier.issue_verified_internal_receipt/);
  assert.match(accepted,/cashier_atomic_tender_receipt_stock_invalid/);
  assert.match(accepted,/cashier_prepare_stock_reservation_invalid/);
  assert.match(accepted,/cashier_tender_replayed_duplicate_payment_or_receipt/);
  assert.match(accepted,/cashier_own_receipt_not_readable_or_incomplete/);
  assert.match(accepted,/manager_read_other_cashier_original_receipt/);
  assert.match(accepted,/cashier_paid_shift_reconciliation_invalid/);
  assert.match(accepted,/cashier_tender_replay_changed_cash_amount/);
  assert.match(accepted,/different_operator_prepared_cashier_shift_sale/);
  assert.match(accepted,/synthetic_sale_or_policy_leaked_past_rollback/);
  assert.match(ledger,/pos_named_cashier_atomic_receipt_authority_missing/);
});

test('synthetic fiscal test data stays isolated and production remains default-denied',()=>{
  assert.match(accepted,/CI synthetic only: never production/);
  assert.match(accepted,/RAISE EXCEPTION 'pos_synthetic_cash_sale_rolled_back' USING ERRCODE='P0001'/);
  assert.match(accepted,/EXCEPTION WHEN SQLSTATE 'P0001' THEN/);
  assert.match(migration,/cashier_receipt_history_requires_forward_recovery/);
  assert.doesNotMatch(migration,/UPDATE pos_cashier.checkout_policies|UPDATE pos_cashier.receipt_policies/);
  assert.doesNotMatch(migration,/payabl|gateway.*capture|refund.*execute/i);
});
