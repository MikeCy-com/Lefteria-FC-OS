import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110158000_PosCashierReconciliationRead.cs');
const api=load('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
const view=load('frontend/src/lib/admin/AdminPosCashierShiftsPanel.svelte');

test('report refuses unassigned staff and rechecks the live identity session',()=>{
  assert.match(sql,/pos.current_cashier_authority\(/);
  assert.match(sql,/v_shift.cashier_id<>v_actor/);
  assert.match(sql,/WHERE organization_id=_owner AND id=_shift/);
  assert.match(sql,/REVOKE ALL ON FUNCTION pos.read_cashier_shift_reconciliation/);
  assert.doesNotMatch(sql,/GRANT EXECUTE|GRANT SELECT/);
});
test('report separately reconciles verified cash, card, refunds, receipts and stock',()=>{
  assert.match(sql,/p.status='verified' AND p.payment_kind='cash'/);
  assert.match(sql,/p.status='verified' AND p.payment_kind='card'/);
  assert.match(sql,/f.status='verified' AND f.refund_kind='cash'/);
  assert.match(sql,/f.status='verified' AND f.refund_kind='card'/);
  assert.match(sql,/rs.released_at IS NULL AND rs.consumed_at IS NULL/);
  assert.match(sql,/receipts_issued/);
  assert.match(sql,/v_shift.opening_cash_cents\+v_cash-v_refunds_cash/);
  assert.match(sql,/pending_card_events/);
  assert.match(sql,/requires_review/);
  assert.match(sql,/v_expected_cash<0/);
  assert.match(view,/Number.isSafeInteger/);
  assert.match(view,/v.expected_cash_cents/);
  assert.match(sql,/v_sales>10000/);
});
test('reconciliation API and front end expose no payment execution',()=>{
  assert.match(api,/MapGet\("\/shifts\/\{shiftId:guid\}\/reconciliation"/);
  assert.match(api,/pos.read_cashier_shift_reconciliation/);
  assert.match(api,/ReadAdminPosCashierReconciliation/);
  assert.match(view,/validReport\(raw,expectedOwner,shift,register\)/);
  assert.match(view,/report.requires_review/);
  assert.match(view,/pos-cashier-reconciliation/);
  assert.match(view,/report.pending_card_events/);
  assert.match(view,/credentials:'same-origin',cache:'no-store'/);
  assert.doesNotMatch(sql,/INSERT INTO pos.payment_events|UPDATE pos.sales/);
});