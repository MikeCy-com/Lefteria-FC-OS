import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const migration = read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115980000_ParentFinanceCurrentGuardianScope.cs');
const fixture = read('../../ops/postgres/academy-enrollment-checks.sql');
const previousFee = read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109100000_AcademyTrainingFeeBilling.cs');
const previousPdf = read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109110000_AcademyInvoiceParity.cs');

const up = migration.split('protected override void Down')[0];
const down = migration.split('protected override void Down')[1];

test('both parent invoice surfaces use current exact child scope while retaining payer identity',()=>{
  assert.match(up,/CREATE OR REPLACE FUNCTION finance\.read_parent_training_fees/);
  assert.match(up,/CREATE OR REPLACE FUNCTION finance\.read_parent_invoice_pdf/);
  assert.equal((up.match(/identity\.current_phone_granted_children\(/g)||[]).length,2);
  assert.match(up,/WHERE payer_identity_id=_identity[\s\S]+?AND EXISTS \([\s\S]+?authorized\.player_id=finance\.training_fee_issues\.player_id/);
  assert.match(up,/authorized\.organization_id=finance\.training_fee_issues\.organization_id/);
  assert.match(up,/f\.payer_identity_id=_identity[\s\S]+?AND EXISTS \([\s\S]+?authorized\.player_id=f\.player_id/);
  assert.match(up,/authorized\.organization_id=f\.organization_id/);
  assert.doesNotMatch(up,/JOIN identity\.mobile_record_links/);
  assert.doesNotMatch(up,/JOIN identity\.canonical_contact_bindings/);
  assert.match(up,/v_count<>cardinality\(_ids\)/);
  assert.match(up,/FROM finance\.training_fee_issues/);
  assert.match(up,/i\.role='parent' AND i\.is_active/);
});

test('scope update changes no issuer, payment, payabl or production behavior',()=>{
  assert.doesNotMatch(up,/CREATE (?:OR REPLACE )?FUNCTION finance\.(?:issue_admin|create_admin|attach_admin|record_admin|mark_training|ready_admin|apply_payabl)/);
  assert.doesNotMatch(up,/UPDATE finance\.|INSERT INTO finance\.|DELETE FROM finance\./);
  assert.match(up,/REVOKE ALL ON FUNCTION finance\.read_parent_training_fees/);
  assert.match(up,/REVOKE ALL ON FUNCTION finance\.read_parent_invoice_pdf/);
  assert.match(down,/JOIN identity\.mobile_record_links/);
  assert.match(down,/JOIN identity\.canonical_contact_bindings/);
  assert.match(previousFee,/CREATE FUNCTION finance\.read_parent_training_fees/);
  assert.match(previousPdf,/CREATE FUNCTION finance\.read_parent_invoice_pdf/);
});

test('disposable PostgreSQL fixtures test exact payer, other child, wrong phone and revocation',()=>{
  for (const marker of [
    'Scoped parent wallet includes an unrelated player fee',
    'Verified original guardian invoice PDF scope failed',
    'Mixed child PDF batch exposed unrelated payer data',
    'Wrong signed parent phone exposed invoice',
    'Other parent received original guardian wallet',
    'Other parent received original guardian invoice',
    'Revoked guardian retained a private fee wallet',
    'Revoked guardian retained private PDF access',
    'finance.read_parent_training_fees(',
    'finance.read_parent_invoice_pdf(',
    'identity.admin_academy_guardian_proof(admin_digest,\'revoke\'',
    'ROLLBACK;'
  ]) assert.ok(fixture.includes(marker),marker);
  assert.ok(fixture.indexOf('Verified original guardian invoice PDF scope failed') <
    fixture.indexOf('guardian_proof(admin_digest,\'revoke\''));
  assert.ok(fixture.indexOf('Revoked guardian retained private PDF access') >
    fixture.indexOf('guardian_proof(admin_digest,\'revoke\''));
});
