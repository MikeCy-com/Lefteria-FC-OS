import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110163000_PosReviewedRefundRequests.cs');
const guards=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110151000_PosLedgerFinancialGuards.cs');
const api=load('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
test('refund request follows approved return and three-person control',()=>{
  assert.match(sql,/pos.current_cashier_authority|identity.lock_admin_club_authority/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/v_return.status<>'approved'/);
  assert.match(sql,/v_actor=v_return.created_by/);
  assert.match(sql,/v_actor=v_return.approved_by/);
  assert.match(sql,/v_payment.status|status='verified'/);
  assert.match(sql,/sale_id=v_return.sale_id/);
});
test('refund instructions remain pending, idempotent and within original payment',()=>{
  assert.match(sql,/CREATE UNIQUE INDEX uq_pos_refund_request/);
  assert.match(sql,/request_id=_request FOR UPDATE/);
  assert.match(sql,/pos_refund_request_replay_mismatch/);
  assert.match(sql,/INSERT INTO pos.refund_events/);
  assert.match(sql,/'pending',v_actor,_request/);
  assert.match(guards,/v_refunded\+NEW.amount_cents>v_payment.amount_cents/);
  assert.match(guards,/v_return_refunds\+NEW.amount_cents>v_returned_cents/);
  assert.match(sql,/INSERT INTO pos.transaction_audit/);
  assert.match(sql,/REVOKE ALL ON FUNCTION pos.request_approved_return_refund/);
  assert.doesNotMatch(sql,/GRANT EXECUTE|POST https|payment_refund|transfer_money/);
});
test('refund request API is guarded and cannot trigger Payabl or cash payout',()=>{
  assert.match(api,/RequestRefundRequest\(Guid RequestId, Guid PaymentEventId, long AmountCents\)/);
  assert.match(api,/pos.request_approved_return_refund/);
  assert.match(api,/RequestAdminPosApprovedReturnRefund/);
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role, "shop:write"\)/);
  assert.doesNotMatch(api,/payabl\.refund|payment_refund|execute_cash_refund/);
});