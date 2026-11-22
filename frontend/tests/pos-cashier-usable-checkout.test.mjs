import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const inventory=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115954000_PosCashierScopedSellableStock.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
const checkout=read('frontend/src/lib/admin/AdminPosCashierCheckoutPanel.svelte');
const shifts=read('frontend/src/lib/admin/AdminPosCashierShiftsPanel.svelte');
const receipt=read('frontend/src/routes/admin/pos/receipts/[receiptId]/+page.svelte');
const grants=read('ops/postgres/pos-cashier-shifts-admin-grants.sql');
const fixture=read('ops/postgres/pos-site-isolation-checks.sql');

test('stock reader is private, bounded and requires the original cashier and open shift',()=>{
  assert.match(inventory,/CREATE FUNCTION pos_cashier.read_assigned_cashier_sellable_stock/);
  assert.match(inventory,/SECURITY DEFINER/);
  assert.match(inventory,/pos_cashier.current_cashier_authority\(_digest,_owner,_register\)/);
  assert.match(inventory,/sh.register_id=_register AND sh.cashier_id=v_actor/);
  assert.match(inventory,/sh.closed_at IS NULL FOR SHARE/);
  assert.match(inventory,/st.location_id=v_location/);
  assert.match(inventory,/JOIN pos.products p/);
  assert.match(inventory,/JOIN pos.categories c/);
  assert.match(inventory,/JOIN pos.product_variants v/);
  assert.match(inventory,/st.on_hand-st.reserved AS available/);
  assert.match(inventory,/LIMIT 150/);
  assert.doesNotMatch(inventory,/['"]cost_cents['"]|['"]on_hand['"]|['"]reserved['"]/);
  assert.match(inventory,/REVOKE ALL ON FUNCTION pos_cashier.read_assigned_cashier_sellable_stock/);
  assert.doesNotMatch(inventory,/GRANT EXECUTE ON FUNCTION|INSERT INTO|UPDATE pos_cashier/);
});
test('fiscal policy is read-only UI gate and server-side transaction remains authoritative',()=>{
  assert.match(inventory,/p.tax_treatment='verified_exempt'/);
  assert.match(inventory,/pos_cashier.receipt_policies/);
  assert.match(checkout,/!stock\.fiscal_policies_approved/);
  assert.match(checkout,/!stock\?\.fiscal_policies_approved/);
  assert.match(checkout,/request_id:prepId,shift_id:shiftAtStart,items:basket/);
  assert.match(checkout,/request_id:tenderId,tendered_cash_cents:amount/);
  assert.match(checkout,/cashConfirmed/);
  assert.match(checkout,/tenderFrozen=true/);
  assert.match(checkout,/v.change_cents!==amount-sale.total_cents/);
  assert.doesNotMatch(checkout,/checkout\.settle|payabl\.capture|window\.location/);
});
test('cashier has exact location product picker and can cancel unpaid reservation',()=>{
  assert.match(endpoint,/MapGet\("\/registers\/\{registerId\}\/sellable-stock"/);
  assert.match(endpoint,/Guid\? shift_id/);
  assert.match(endpoint,/pos_cashier.read_assigned_cashier_sellable_stock/);
  assert.match(shifts,/AdminPosCashierCheckoutPanel/);
  assert.match(shifts,/shiftId=\{selectedRegister.shift_id\}/);
  assert.match(checkout,/data-testid="pos-cashier-safe-checkout"/);
  assert.match(checkout,/Μόνο προϊόντα και διαθέσιμο απόθεμα της δικής σας/);
  assert.match(checkout,/\/sales\/prepare\?/);
  assert.match(checkout,/\/cancel\?/);
  assert.match(checkout,/\/tender-cash\?/);
  assert.match(checkout,/prepared\.total_cents/);
  assert.match(checkout,/Προβολή & εκτύπωση αποθηκευμένης απόδειξης/);
  assert.match(receipt,/user\.role==='cashier'/);
  assert.match(receipt,/shop:operate/);
  assert.doesNotMatch(checkout,/localStorage|sessionStorage|{@html|card_number|payment_card/);
});
test('manual read grant never enables settlement, raw tables or refunds',()=>{
  assert.match(grants,/read_assigned_cashier_sellable_stock/);
  assert.match(grants,/has_table_privilege\(a,format/);
  assert.doesNotMatch(grants,/GRANT EXECUTE ON FUNCTION pos_cashier\.(prepare_cashier_checkout|settle_exact_cash_sale|issue_verified_internal_receipt)/);
  assert.doesNotMatch(grants,/GRANT (SELECT|UPDATE|INSERT|DELETE) ON pos_cashier/);
  assert.match(fixture,/cashier_stock_read_is_not_private_or_policy_gated/);
  assert.match(fixture,/cashier_read_other_city_stock/);
  assert.match(fixture,/cashier_read_other_organization_stock/);
  assert.match(fixture,/manager_borrowed_other_cashier_shift_stock/);
  assert.match(fixture,/synthetic_sale_or_policy_leaked_past_rollback/);
});
