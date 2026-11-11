import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const schema=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110154000_PosInventoryReservedCheckout.cs');
const fulfillment=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110156000_PosReceiptInventoryFulfillment.cs');
const guard=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110151000_PosLedgerFinancialGuards.cs');
const cancellation=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110155000_PosUnpaidSaleCancellation.cs');

test('receipt issuance consumes only currently reserved stock',()=>{
  assert.match(fulfillment,/AFTER INSERT ON pos.receipts/);
  assert.match(fulfillment,/v_sale.status<>'paid'/);
  assert.match(fulfillment,/v_line_count<>v_reserved_count/);
  assert.match(fulfillment,/inventory_receipt_reservation_line_mismatch/);
  assert.match(fulfillment,/rs.quantity<>sl.quantity/);
  assert.match(fulfillment,/ORDER BY rs.location_id,rs.product_id,rs.variant_id/);
  assert.match(fulfillment,/on_hand=on_hand-v_row.quantity/);
  assert.match(fulfillment,/reserved=reserved-v_row.quantity/);
  assert.match(fulfillment,/version=version\+1/);
  assert.match(fulfillment,/reserved>=v_row.quantity AND on_hand>=v_row.quantity/);
  assert.match(fulfillment,/consumed_at=clock_timestamp/);
  assert.match(fulfillment,/inventory_receipt_double_consumption/);
  assert.match(fulfillment,/fulfilled_stock_requires_forward_recovery/);
});
test('unpaid cancelled stock cannot also be consumed',()=>{
  assert.match(schema,/CHECK\(released_at IS NULL OR consumed_at IS NULL\)/);
  assert.match(cancellation,/released_at IS NULL AND consumed_at IS NULL/);
  assert.match(fulfillment,/released_at IS NULL AND consumed_at IS NULL/);
  assert.match(cancellation,/p.status IN \('pending','verified'\)/);
  assert.match(guard,/receipt_requires_paid_sale/);
});
test('receipt fulfillment has no public or automatic runtime permission',()=>{
  assert.match(fulfillment,/REVOKE ALL ON FUNCTION pos.fulfill_receipt_reserved_inventory\(\) FROM PUBLIC/);
  assert.doesNotMatch(fulfillment,/GRANT EXECUTE|GRANT INSERT|CREATE POLICY/);
});