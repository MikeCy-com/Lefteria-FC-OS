import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const source=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110152000_PosVerifiedReceiptIssuer.cs');
test('internal receipt issuer refuses without approved fiscal policy and current manager',()=>{
 assert.match(source,/enabled boolean NOT NULL DEFAULT false/);
 assert.match(source,/fiscal_receipt_policy_disabled/);
 assert.match(source,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
 assert.match(source,/v_role NOT IN \('manager','super_admin'\)/);
 assert.match(source,/REVOKE ALL ON FUNCTION pos_cashier\.issue_verified_internal_receipt/);
 assert.doesNotMatch(source,/GRANT EXECUTE|MapPost|HttpPost/);
});
test('issuer locks sale and only accepts exactly reconciled verified payment',()=>{
 assert.match(source,/WHERE organization_id=_owner AND id=_sale FOR UPDATE/);
 assert.match(source,/v_verified<>v_sale\.total_cents/);
 assert.match(source,/v_line_sum<>v_sale\.subtotal_cents/);
 assert.match(source,/status='verified'/);
 assert.match(source,/receipt_requires_exact_verified_payment/);
 assert.match(source,/status='paid',paid_at=clock_timestamp\(\)/);
});
test('receipt number is database-serialized and payload snapshotted',()=>{
 assert.match(source,/ON CONFLICT\(organization_id,register_id\) DO NOTHING/);
 assert.match(source,/SET next_number=next_number\+1/);
 assert.match(source,/RETURNING next_number-1 INTO v_number/);
 assert.match(source,/jsonb_build_object\(/);
 assert.match(source,/v_data,encode\(sha256\(convert_to\(v_data::text,'UTF8'\)\),'hex'\)\)/);
 assert.match(source,/WHERE organization_id=_owner AND sale_id=_sale/);
 assert.match(source,/receipt_number',v_exists\.receipt_number/);
 assert.match(source,/INSERT INTO pos_cashier\.transaction_audit/);
 assert.match(source,/INSERT INTO audit\.security_events/);
});