import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';

const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const verifier=load('backend/src/LefteriaFc.Api/Pos/PayablNotificationVerifier.cs');
const inbox=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110160000_PosPayablNotificationInbox.cs');
const vat=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110159000_PosCyprusVatCatalog.cs');
const returns=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110161000_PosReturnManagerReview.cs');

test('payabl documented simplified SHA-256 notification fixture',()=>{
  const digest=createHash('sha256').update('118656640capture01610018172goodsecret').digest('hex');
  assert.equal(digest,'1f67d79aa5e2a4070b2091837fefae84cd15f08370de0cee4bf9ea75951e047b');
  assert.match(verifier,/SHA256.HashData\(Encoding.UTF8.GetBytes\(input\)\)/);
  assert.match(verifier,/id \+ kind \+ errorCode \+ textTimestamp \+ merchantSecret/);
  assert.match(verifier,/CryptographicOperations.FixedTimeEquals/);
  assert.match(verifier,/IsSuccessfulCapture/);
  assert.match(verifier,/value.Type == "capture" && value.ErrorCode == "0"/);
  assert.match(verifier,/maxAge > TimeSpan.FromHours\(24\)/);
  assert.match(verifier,/receivedAt.AddMinutes\(5\)/);
});
test('signed Payabl notifications alone cannot change paid status or issue receipts',()=>{
  assert.match(inbox,/status varchar\(24\) NOT NULL DEFAULT 'pending_recheck'/);
  assert.match(inbox,/provider_rechecked_at/);
  assert.match(inbox,/UNIQUE\(merchant_id,provider_transaction_id,notification_type,gateway_error_code\)/);
  assert.match(inbox,/UNIQUE\(organization_id,evidence_sha256\)/);
  assert.match(inbox,/REVOKE ALL ON pos_cashier.payabl_notification_inbox FROM PUBLIC/);
  assert.match(inbox,/ALTER TABLE pos_cashier.payabl_notification_inbox ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(inbox,/INSERT INTO pos_cashier.payment_events|UPDATE pos_cashier.sales|INSERT INTO pos_cashier.receipts/);
  assert.doesNotMatch(verifier,/MapPost\(|Results.Ok\(|ExecuteNonQueryAsync/);
});
test('VAT classifications are per product variant and require explicit approval',()=>{
  for (const value of ['standard_19','reduced_9','reduced_5','reduced_3','zero_0','exempt'])
    assert.ok(vat.includes("'"+value+"'"));
  assert.match(vat,/enabled boolean NOT NULL DEFAULT false/);
  assert.match(vat,/basis_points IS NULL/);
  assert.match(vat,/classification_note/);
  assert.match(vat,/authority_reference/);
  assert.match(vat,/effective_from date NOT NULL/);
  assert.match(vat,/FOREIGN KEY\(organization_id,product_id,variant_id\)/);
  assert.match(vat,/ALTER TABLE pos_cashier.vat_treatments ENABLE ROW LEVEL SECURITY/);
  assert.match(vat,/ALTER TABLE pos_cashier.variant_vat_assignments ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(vat,/GRANT EXECUTE|GRANT SELECT|GRANT INSERT/);
});
test('return request requires receipted paid sale and bounded original lines',()=>{
  assert.match(returns,/v_sale.status<>'paid'/);
  assert.match(returns,/pos_cashier.receipts/);
  assert.match(returns,/pos_cashier.current_cashier_authority/);
  assert.match(returns,/jsonb_array_length\(_lines\) NOT BETWEEN 1 AND 30/);
  assert.match(returns,/count\(DISTINCT/);
  assert.match(returns,/INSERT INTO pos_cashier.return_lines/);
  assert.match(returns,/pos_return_replay_mismatch/);
  assert.match(returns,/return_request/);
});
test('return approval requires different manager with valid current organization grant',()=>{
  assert.match(returns,/identity.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(returns,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(returns,/v_return.created_by=v_actor/);
  assert.match(returns,/pos_return_self_approval_denied/);
  assert.match(returns,/v_return.status<>'requested'/);
  assert.match(returns,/review_request_id=_request/);
  assert.match(returns,/return_approve/);
  assert.match(returns,/return_reject/);
  assert.match(returns,/REVOKE ALL ON FUNCTION pos_cashier.review_cashier_return/);
  assert.doesNotMatch(returns,/INSERT INTO pos_cashier.refund_events|UPDATE pos_cashier.payment_events/);
});