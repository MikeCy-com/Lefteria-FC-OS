import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const source=(p)=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const checkout=source('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110154000_PosInventoryReservedCheckout.cs');
const voider=source('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110155000_PosUnpaidSaleCancellation.cs');
const guards=source('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110151000_PosLedgerFinancialGuards.cs');
const api=source('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');

test('staged checkout has no automatic execution grant or enabled tax policy',()=>{
  assert.match(checkout,/enabled boolean NOT NULL DEFAULT false/);
  assert.match(checkout,/tax_treatment='verified_exempt'/);
  assert.match(checkout,/checkout_tax_policy_disabled/);
  assert.match(checkout,/REVOKE ALL ON FUNCTION pos.prepare_cashier_checkout/);
  assert.doesNotMatch(checkout,/GRANT EXECUTE|GRANT INSERT|GRANT UPDATE|CREATE POLICY/);
  assert.match(checkout,/ALTER TABLE pos.sale_reservations ENABLE ROW LEVEL SECURITY/);
});
test('sale is prepared only for an authorized cashier and open shift',()=>{
  assert.match(checkout,/pos.current_cashier_authority\(_digest,_owner,_register\)/);
  assert.match(checkout,/register_id=_register AND cashier_id=v_actor FOR UPDATE/);
  assert.match(checkout,/v_shift.closed_at IS NOT NULL/);
  assert.match(checkout,/r.is_active FOR SHARE/);
  assert.match(checkout,/UNIQUE/); // unique replay belongs to the index/constraint migration
});
test('cart validation disallows price injection, unknown shapes, duplicate variants',()=>{
  assert.match(checkout,/jsonb_array_length\(_items\) NOT BETWEEN 1 AND 30/);
  assert.match(checkout,/jsonb_object_keys\(e.value\)/);
  assert.match(checkout,/\?& ARRAY\['product_id','variant_id','quantity'\]/);
  assert.match(checkout,/count\(DISTINCT/);
  assert.match(checkout,/checkout_items_invalid_or_duplicate/);
  assert.match(checkout,/checkout_replay_mismatch/);
  assert.match(checkout,/checkout_request_sha256/);
  assert.doesNotMatch(checkout,/value->>'price_cents'|value->>'tax_cents'|value->>'discount_cents'/);
});
test('stock is serially reserved with exact server prices and atomic snapshots',()=>{
  assert.match(checkout,/ORDER BY product_id,variant_id/);
  assert.match(checkout,/v_stock.on_hand-v_stock.reserved<v_line.quantity/);
  assert.match(checkout,/FOR UPDATE/);
  assert.match(checkout,/v_price.price_cents/);
  assert.match(checkout,/INSERT INTO pos.sale_lines/);
  assert.match(checkout,/reserved=reserved\+v_line.quantity,version=version\+1/);
  assert.match(checkout,/on_hand-reserved>=v_line.quantity/);
  assert.match(checkout,/INSERT INTO pos.sale_reservations/);
  assert.match(guards,/pos_sale_line_server_price/);
});
test('unpaid sale cancellation is idempotent and rejects pending or captured money',()=>{
  assert.match(voider,/CREATE UNIQUE INDEX uq_pos_void_request/);
  assert.match(voider,/pos.current_cashier_authority\(_digest,_owner,v_register\)/);
  assert.match(voider,/v_sale.cashier_id<>v_actor/);
  assert.match(voider,/v_sale.void_request_id IS DISTINCT FROM _request/);
  assert.match(voider,/p.status IN \('pending','verified'\)/);
  assert.match(voider,/pos.receipts r/);
  assert.match(voider,/reserved=reserved-v_row.quantity,version=version\+1/);
  assert.match(voider,/released_at=clock_timestamp/);
  assert.match(voider,/status='voided',voided_at=clock_timestamp/);
  assert.match(voider,/pos.transaction_audit/);
  assert.match(voider,/REVOKE ALL ON FUNCTION pos.cancel_unpaid_cashier_sale/);
});
test('API only accepts item identifiers, quantity, request and shift IDs',()=>{
  assert.match(api,/PrepareSaleRequest\(Guid RequestId, Guid ShiftId, List<CheckoutItem>\? Items\)/);
  assert.match(api,/CheckoutItem\(string ProductId, string VariantId, int Quantity\)/);
  assert.match(api,/Distinct\(\)\.Count\(\) != body.Items.Count/);
  assert.match(api,/NpgsqlDbType.Jsonb/);
  assert.match(api,/pos.prepare_cashier_checkout/);
  assert.match(api,/pos.cancel_unpaid_cashier_sale/);
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.doesNotMatch(api,/capture_card|execute_refund|MapPost\("\/payments|MapPost\("\/receipts/);
});
test('older financial guards have properly balanced PostgreSQL delimiters',()=>{
  assert.doesNotMatch(guards,/AS \$(?!\$|[a-zA-Z_])/);
  assert.doesNotMatch(guards,/END \$(?!\$|[a-zA-Z_])/);
  assert.equal((guards.match(/AS \$\$/g)||[]).length,(guards.match(/END \$\$;/g)||[]).length);
});
