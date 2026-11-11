import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110162000_PosProtectedReceiptRead.cs');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
const page=read('frontend/src/routes/admin/pos/receipts/[receiptId]/+page.svelte');
const ret=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110161000_PosReturnManagerReview.cs');

test('receipt read requires cashier assignment, original sale and exact organization',()=>{
  assert.match(sql,/pos_cashier.current_cashier_authority\(/);
  assert.match(sql,/v_sale.cashier_id<>v_actor/);
  assert.match(sql,/v_sale.register_id<>v_receipt.register_id/);
  assert.match(sql,/v_sale.status<>'paid'/);
  assert.match(sql,/v_receipt.immutable_payload/);
  assert.match(sql,/v_actual_hash IS DISTINCT FROM v_receipt.payload_sha256/);
  assert.match(sql,/pos_receipt_integrity_failed/);
  assert.match(sql,/REVOKE ALL ON FUNCTION pos_cashier.read_assigned_cashier_receipt/);
  assert.doesNotMatch(sql,/GRANT EXECUTE|UPDATE pos|INSERT INTO pos/);
});
test('receipt API reads protected snapshot and cannot issue a new invoice',()=>{
  assert.match(api,/MapGet\("\/receipts\/\{receiptId:guid\}"/);
  assert.match(api,/pos_cashier.read_assigned_cashier_receipt/);
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role, "shop:write"\)/);
  assert.doesNotMatch(api,/MapPost\("\/receipts|issue_verified_internal_receipt/);
});
test('print page rejects malformed snapshots and uses explicit user action',()=>{
  assert.match(page,/safeReceipt\(value,org,id\)/);
  assert.match(page,/receipt=null;error=''/);
  assert.match(page,/new URLSearchParams\(\{organization_id:org\}\)/);
  assert.match(page,/response.status===401\|\|response.status===403/);
  assert.match(page,/p.lines.reduce/);
  assert.match(page,/p.discount_cents as number/);
  assert.match(page,/window.print\(\)/);
  assert.match(page,/@media print/);
  assert.match(page,/payload_sha256/);
  assert.doesNotMatch(page,/localStorage|sessionStorage|dangerouslySetInnerHTML|\{@html/);
});
test('return workflow does not perform any live cash or card refund',()=>{
  assert.match(ret,/pos_return_self_approval_denied/);
  assert.match(ret,/review_request_id=_request/);
  assert.match(api,/pos_cashier.request_assigned_cashier_return/);
  assert.match(api,/pos_cashier.review_cashier_return/);
  assert.match(api,/RequestAdminPosSaleReturn/);
  assert.match(api,/ReviewAdminPosSaleReturn/);
  assert.doesNotMatch(api,/pos_cashier.execute_card_refund|pos_cashier.execute_cash_refund/);
});