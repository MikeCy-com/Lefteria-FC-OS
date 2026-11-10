import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=(p)=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const schema=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110150000_PosCashierTransactionLedger.cs');
const guards=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110151000_PosLedgerFinancialGuards.cs');

test('cashiers use real register, location and session assignments',()=>{
 for(const name of ['locations','registers','cashier_assignments','shifts','stock'])
   assert.match(schema,new RegExp('CREATE TABLE pos\\.'+name+' \\('));
 assert.match(schema,/FOREIGN KEY\(organization_id,register_id,cashier_id\)/);
 assert.match(schema,/uq_pos_one_open_shift/);
 assert.match(schema,/reserved>=0 AND reserved<=on_hand/);
 assert.match(schema,/FOREIGN KEY\(organization_id,product_id,variant_id\)/);
});
test('sale pricing is snapshotted in integer cents with idempotent checkout',()=>{
 assert.match(schema,/UNIQUE\(organization_id,request_id\)/);
 assert.match(schema,/quantity BETWEEN 1 AND 99/);
 assert.match(schema,/unit_price_cents integer NOT NULL/);
 assert.match(schema,/line_total_cents bigint GENERATED ALWAYS AS/);
 assert.match(schema,/total_cents=subtotal_cents-discount_cents\+tax_cents/);
 assert.match(guards,/v_lines<>NEW.subtotal_cents/);
 assert.match(guards,/v_total<>NEW.total_cents/);
 assert.match(guards,/pos_sale_lines_immutable/);
 assert.match(guards,/sale_financial_snapshot_immutable/);
});
test('card verification requires evidence, receipt demands reconciled paid sale',()=>{
 assert.match(schema,/payment_kind IN \('cash','card'\)/);
 assert.match(schema,/provider_evidence_sha256 ~ '\^\[0-9a-f\]\{64\}\$'/);
 assert.match(schema,/uq_pos_card_provider_capture/);
 assert.match(guards,/payment_event_mutation_denied/);
 assert.match(guards,/verified_payments_exceed_sale_total/);
 assert.match(guards,/receipt_requires_paid_sale/);
 assert.match(guards,/receipt_snapshot_not_reconciled/);
 assert.match(schema,/UNIQUE\(organization_id,register_id,receipt_number\)/);
 assert.match(schema,/UNIQUE\(organization_id,sale_id\)/);
 assert.match(guards,/pos_receipts_immutable/);
});
test('returns and refunds never exceed historical items or verified payment',()=>{
 assert.match(guards,/return_sale_mismatch/);
 assert.match(guards,/return_quantity_exceeds_sale/);
 assert.match(guards,/refund_payment_mismatch/);
 assert.match(guards,/refund_exceeds_verified_payment_or_return_value/);
 assert.match(guards,/v_return_refunds\+NEW.amount_cents>v_returned_cents/);
 assert.match(guards,/v_refunded\+NEW.amount_cents>v_payment.amount_cents/);
 assert.match(guards,/refund_confirmation_invalid/);
 assert.match(schema,/uq_pos_provider_refund/);
});
test('POS staging is default deny and no live cashier API grants are created',()=>{
 for(const table of ['locations','registers','cashier_assignments','shifts','stock','sales',
 'sale_lines','payment_events','receipt_sequences','receipts','returns','return_lines',
 'refund_events','transaction_audit'])
   assert.match(schema,new RegExp('ALTER TABLE pos\\.'+table+' ENABLE ROW LEVEL SECURITY'));
 assert.match(schema,/REVOKE ALL ON ALL TABLES IN SCHEMA pos FROM PUBLIC/);
 assert.doesNotMatch(schema,/GRANT (?:INSERT|SELECT|UPDATE|DELETE|EXECUTE)|CREATE POLICY/);
 assert.match(schema,/pos_financial_history_requires_forward_recovery/);
 assert.match(guards,/pos_financial_history_is_immutable/);
});
