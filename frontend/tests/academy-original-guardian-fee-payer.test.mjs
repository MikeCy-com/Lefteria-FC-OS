import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261121152000_AcademyOriginalGuardianFeePayer.cs');
const original=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115940000_FinanceGlobalRoleBoundary.cs');
const proof=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115970000_AcademyGuardianOtpScope.cs');
const fixtures=read('ops/postgres/academy-enrollment-checks.sql');
const up=migration.split('protected override void Down')[0];
const down=migration.split('protected override void Down')[1];

test('academy original OTP guardian is a fallback payer only after current verified child proof',()=>{
  assert.match(up,/CREATE OR REPLACE FUNCTION finance\.issue_admin_training_fee/);
  assert.match(up,/v_role<>'super_admin'/);
  assert.match(up,/JOIN identity\.mobile_record_links l/);
  assert.match(up,/JOIN identity\.canonical_contact_bindings b/);
  assert.match(up,/IF NOT FOUND THEN[\s\S]+?JOIN identity\.academy_guardian_otp_claims verified_otp/);
  assert.match(up,/JOIN LATERAL identity\.current_academy_guardian_claims\(/);
  assert.match(up,/current_claim\.registration_id=placement\.registration_id/);
  assert.match(up,/current_claim\.player_id=p\.id/);
  assert.match(up,/current_claim\.organization_id=p\.organization_id/);
  assert.match(up,/i\.role='parent' AND i\.is_active/);
  assert.match(up,/p\.is_active AND NOT p\.is_archived[\s\S]+?NOT p\.is_transferred/);
  assert.match(up,/v_verified_candidates<>1/);
  assert.match(up,/verified_parent_and_child_required/);
  assert.doesNotMatch(up,/identity\.current_phone_granted_children\(/);
  assert.doesNotMatch(up,/identity\.valid_guardian_phone_grants\(/);
  assert.match(proof,/claim\.expires_at>statement_timestamp\(\)/);
  assert.match(proof,/proof\.status='verified_proof'/);
  assert.match(proof,/consent_kind IN \('participation','medical_authorization'/);
});

test('no change to the exact month issuance, current payer snapshots, existing importer, or collection',()=>{
  assert.match(original,/CREATE OR REPLACE FUNCTION finance\.issue_admin_training_fee/);
  assert.match(up,/ON CONFLICT \(organization_id,player_id,fee_month\) DO NOTHING/);
  assert.match(up,/v_row\.payer_identity_id<>v_payer/);
  assert.match(up,/v_row\.amount_cents<>_cents/);
  assert.match(up,/fee_month_already_issued_differently/);
  assert.match(up,/VALUES\(_id,_owner,_player,v_payer,_month,_cents,'\+'\|\|v_payer_phone/);
  assert.match(up,/month < DATE '2020-01-01'|_month < DATE '2020-01-01'/);
  assert.doesNotMatch(up,/create_admin_payment_request|apply_payabl_notification|INSERT INTO finance\.payment_requests/);
  assert.doesNotMatch(up,/AUTOMATIC|automatic_collection_enabled|INSERT INTO finance\.general_charge_issues/);
});

test('down migration restores legacy-only issuance without deleting historical invoices',()=>{
  assert.match(down,/CREATE OR REPLACE FUNCTION finance\.issue_admin_training_fee/);
  assert.match(down,/JOIN identity\.mobile_record_links l/);
  assert.match(down,/JOIN identity\.canonical_contact_bindings b/);
  assert.doesNotMatch(down,/JOIN identity\.academy_guardian_otp_claims/);
  assert.doesNotMatch(down,/DROP TABLE|DELETE FROM finance\.training_fee_issues/);
});

test('rollback-only SQL checks actual payer identity, one month, replay, wallet and revoked proof',()=>{
  for(const evidence of [
    'New Academy original guardian could not receive an exact month fee',
    'Original guardian training fee replay created a duplicate',
    'Issued new Academy fee not visible to its exact verified payer',
    'New Academy payer invoice PDF verification failed',
    'Revoked original guardian was assigned a new Academy fee',
    'Denied Academy fee issuance wrote a private record',
    'finance.issue_admin_training_fee(',
    'finance.read_parent_invoice_pdf(',
    'ROLLBACK;'
  ]) assert.ok(fixtures.includes(evidence),'Missing '+evidence);
  assert.ok(fixtures.indexOf('Original guardian training fee replay created a duplicate')<
    fixtures.indexOf("guardian_proof(admin_digest,'revoke'"));
  assert.ok(fixtures.indexOf('Revoked original guardian was assigned a new Academy fee')>
    fixtures.indexOf("guardian_proof(admin_digest,'revoke'"));
});
