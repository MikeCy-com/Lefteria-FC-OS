import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const source=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110164000_PosFinancePostingOutbox.cs');
const receipt=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110152000_PosVerifiedReceiptIssuer.cs');
const refund=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110163000_PosReviewedRefundRequests.cs');

test('financial outbox is private, immutable, organization-scoped and idempotent',()=>{
  assert.match(source,/CREATE TABLE pos_cashier.finance_posting_outbox/);
  assert.match(source,/CHECK\(event_kind IN \('sale_receipted','refund_verified'\)\)/);
  assert.match(source,/UNIQUE\(organization_id,event_kind,source_id\)/);
  assert.match(source,/FOREIGN KEY\(organization_id,sale_id\)/);
  assert.match(source,/ALTER TABLE pos_cashier.finance_posting_outbox ENABLE ROW LEVEL SECURITY/);
  assert.match(source,/REVOKE ALL ON pos_cashier.finance_posting_outbox FROM PUBLIC/);
  assert.match(source,/BEFORE UPDATE OR DELETE ON pos_cashier.finance_posting_outbox/);
  assert.match(source,/pos_finance_posting_immutable/);
  assert.doesNotMatch(source,/GRANT SELECT|GRANT INSERT|GRANT UPDATE|GRANT DELETE/);
});
test('only exact paid and cryptographically hashed issued receipts enqueue sale posting',()=>{
  assert.match(source,/AFTER INSERT ON pos_cashier.receipts/);
  assert.match(source,/v_sale.status<>'paid'/);
  assert.match(source,/NEW.payload_sha256 IS DISTINCT FROM/);
  assert.match(source,/NEW.immutable_payload->>'total_cents'/);
  assert.match(source,/pos_finance_receipt_not_reconciled/);
  assert.match(source,/ON CONFLICT\(organization_id,event_kind,source_id\) DO NOTHING/);
  assert.match(receipt,/INSERT INTO pos_cashier.receipts/);
});
test('only verified original-payment refunds enqueue the reversal, never pending',()=>{
  assert.match(source,/NEW.status<>'verified'/);
  assert.match(source,/AFTER INSERT OR UPDATE OF status ON pos_cashier.refund_events/);
  assert.match(source,/v_payment.status<>'verified'/);
  assert.match(source,/v_payment.sale_id<>v_return.sale_id/);
  assert.match(source,/v_payment.payment_kind<>NEW.refund_kind/);
  assert.match(source,/v_sale.status<>'paid'/);
  assert.match(source,/pos_finance_refund_not_reconciled/);
  assert.match(refund,/'pending',v_actor,_request/);
});
test('finance outbox cannot silently change Academy player or parent balances',()=>{
  assert.doesNotMatch(source,/UPDATE (?:academy|finance|club)\.|INSERT INTO (?:academy|finance|club)\./);
  assert.doesNotMatch(source,/account_balance_paid|auto_mark_paid|parent_id/);
  assert.match(source,/finance_posting_history_requires_forward_recovery/);
});