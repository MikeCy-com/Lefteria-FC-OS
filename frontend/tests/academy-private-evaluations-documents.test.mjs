import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110100000_AcademyPrivateEvaluationsDocuments.cs',import.meta.url),'utf8');
test('Both club organizations, real player foreign keys and 24-hour snapshot rule',()=>{
  assert.match(source,/organization_id IN \('lefteria_fc','lefteria_fc_academy'\)/);
  assert.match(source,/FOREIGN KEY \(organization_id,player_id\)/g);
  assert.match(source,/edit_until=created_at\+interval '24 hours'/);
  assert.match(source,/CREATE TABLE academy_private\.evaluation_revisions/);
  assert.match(source,/PRIMARY KEY\(evaluation_id,revision\)/);
  assert.match(source,/CREATE UNIQUE INDEX uq_academy_evaluation_legacy/);
  assert.doesNotMatch(source,/first_team_extras|body_fat_percent|injury_status/);
});
test('original document categories and mandatory expiries retained',()=>{
  const catalog=['id_passport','birth_certificate','photo','registration_form',
    'kop_registration','transfer_certificate','medical_cert','vaccination_record',
    'consent_form','gdpr_consent','insurance','school_confirmation','image_rights','other'];
  for(const type of catalog)assert.ok(source.includes("'"+type+"'"),type);
  assert.match(source,/expiry_date IS NOT NULL/);
  assert.match(source,/CREATE INDEX ix_academy_document_expiry/);
  assert.match(source,/UNIQUE INDEX uq_academy_document_legacy/);
});
test('private files are referenced by opaque object ID, not public paths',()=>{
  assert.match(source,/storage_object_id uuid NOT NULL UNIQUE/);
  assert.match(source,/encryption_key_id varchar\(120\) NOT NULL/);
  assert.match(source,/sha256 ~ '\^\[0-9a-f\]\{64\}\$'/);
  assert.match(source,/size_bytes BETWEEN 1 AND 10485760/);
  assert.doesNotMatch(source,/public_url|file_path|\/api\/uploads/);
});
test('no app grants or RLS bypass, audits are private',()=>{
  for(const name of ['evaluations','evaluation_revisions','player_documents','document_audit_events'])
    assert.ok(source.includes('ALTER TABLE academy_private.'+name+' ENABLE ROW LEVEL SECURITY'),name);
  assert.match(source,/REVOKE ALL ON ALL TABLES IN SCHEMA academy_private FROM PUBLIC/);
  assert.doesNotMatch(source,/GRANT (?:SELECT|INSERT|UPDATE|DELETE|EXECUTE)|CREATE POLICY/i);
  assert.match(source,/request_id uuid NOT NULL UNIQUE/);
  assert.match(source,/DROP SCHEMA academy_private/);
});
