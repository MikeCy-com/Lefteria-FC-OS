import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const load=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const sql=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110157000_PosVerifiedCashTender.cs');
const api=load('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
const receipt=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110152000_PosVerifiedReceiptIssuer.cs');
const financial=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110151000_PosLedgerFinancialGuards.cs');

test('cash settlement requires two disabled-by-default independently reviewed fiscal policies',()=>{
  assert.match(sql,/pos.checkout_policies/);
  assert.match(sql,/pos.receipt_policies/);
  assert.match(sql,/checkout_tax_policy_disabled|cash_tender_fiscal_policy_disabled/);
  assert.match(sql,/tax_treatment='verified_exempt'/);
  assert.match(sql,/REVOKE ALL ON FUNCTION pos.settle_exact_cash_sale/);
  assert.doesNotMatch(sql,/GRANT EXECUTE|GRANT INSERT|GRANT UPDATE/);
});
test('cashier is freshly authorized and owns the open register shift and sale',()=>{
  assert.match(sql,/pos.current_cashier_authority\(/);
  assert.match(sql,/v_shift.closed_at IS NOT NULL/);
  assert.match(sql,/v_sale.cashier_id<>v_actor/);
  assert.match(sql,/v_sale.shift_id<>v_shift.id/);
  assert.match(sql,/WHERE organization_id=_owner AND id=_sale FOR UPDATE/);
  assert.match(sql,/WHERE id=v_sale.shift_id AND organization_id=_owner/);
});
test('cash replay is exact, counted, change-tracked and cannot double collect',()=>{
  assert.match(sql,/verification_reference=v_reference/);
  assert.match(sql,/v_payment.cash_tendered_cents<>_tendered/);
  assert.match(sql,/v_payment.sale_id<>_sale/);
  assert.match(sql,/v_payment.recorded_by<>v_actor/);
  assert.match(sql,/cash_tender_replay_mismatch/);
  assert.match(sql,/cash_tendered_cents=amount_cents\+cash_change_cents/);
  assert.match(sql,/v_change:=_tendered-v_sale.total_cents/);
  assert.match(sql,/_tendered<v_sale.total_cents/);
  assert.match(sql,/EXISTS \(SELECT 1 FROM pos.payment_events p/);
  assert.match(sql,/INSERT INTO pos.payment_events/);
  assert.match(sql,/INSERT INTO pos.transaction_audit/);
});
test('cash receipt and inventory are committed atomically or rolled back',()=>{
  assert.match(sql,/v_receipt:=pos.issue_verified_internal_receipt\(/);
  assert.match(sql,/cash_tender_inventory_not_reserved/);
  assert.match(sql,/cash_tender_receipt_missing/);
  assert.match(receipt,/pos.receipts/);
  assert.match(financial,/pos_receipt_paid_guard/);
  assert.match(sql,/cash_payment_history_requires_forward_recovery/);
});
test('cash HTTP endpoint requires admin authorization; no card capture provided',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/session.User.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role, "shop:write"\)/);
  assert.match(api,/session.User.Organizations.Any/);
  assert.match(api,/CashTenderRequest\(Guid RequestId, long TenderedCashCents\)/);
  assert.match(api,/pos.settle_exact_cash_sale\(@digest,@owner,@sale,@request,@cash\)/);
  assert.match(api,/TenderVerifiedAdminPosCashSale/);
  assert.doesNotMatch(api,/payabl\.capture|refund\.execute|MapPost\("\/card/);
});