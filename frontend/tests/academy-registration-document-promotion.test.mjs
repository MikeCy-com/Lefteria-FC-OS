import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const migration=read('../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261122173000_AcademyRegistrationDocumentPromotion.cs');
const sql=read('../ops/postgres/academy-enrollment-checks.sql');
const upload=read('../backend/src/LefteriaFc.Api/Evaluations/AdminPrivateDocumentUploadEndpoints.cs');
const scripts=read('../scripts/workspace/run-integration-tests.sh');
const ci=read('../.github/workflows/rewrite-build.yml');

test('registration document holding vault is deny-by-default and per child, not public media',()=>{
  assert.match(migration,/CREATE TABLE academy_intake\.registration_document_escrow/);
  assert.match(migration,/registration_id uuid NOT NULL\s+REFERENCES academy_intake\.registrations/);
  assert.match(migration,/UNIQUE\(registration_id,document_slot\)/);
  assert.match(migration,/document_slot IN\s*\('id_front','id_back','birth_certificate','passport_photo'\)/);
  assert.match(migration,/REVOKE ALL ON academy_intake\.registration_document_escrow FROM PUBLIC/);
  assert.match(migration,/ALTER TABLE academy_intake\.registration_document_escrow ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(migration,/GRANT (?:EXECUTE|SELECT|INSERT|UPDATE|DELETE)/);
  assert.doesNotMatch(migration,/media_library|public_media|image_url/);
});
test('on approved roster placement ciphertext moves to the exact private Player Documents vault',()=>{
  assert.match(migration,/AFTER INSERT ON academy_intake\.registration_placements/);
  assert.match(migration,/WHERE e\.registration_id=NEW\.registration_id/);
  assert.match(migration,/NEW\.organization_id,NEW\.player_id/);
  assert.match(migration,/INSERT INTO academy_private\.encrypted_document_objects/);
  assert.match(migration,/INSERT INTO academy_private\.player_documents/);
  assert.match(migration,/INSERT INTO academy_private\.document_audit_events/);
  assert.match(migration,/INSERT INTO academy_intake\.registration_document_promotions/);
  assert.match(migration,/ciphertext=NULL,nonce=NULL,auth_tag=NULL/);
  assert.match(migration,/o\.scan_status='quarantined'|scan_status\)\s*VALUES/);
  assert.match(upload,/academy_private\.quarantine_admin_player_document/);
});
test('rollback-only DB fixture asserts 1,2,4,5 and denies cross-city manager reads',()=>{
  assert.match(sql,/FOR sibling_index IN 2\.\.5 LOOP/);
  assert.match(sql,/processed IN \(2,4,5\)/);
  assert.match(sql,/Unapproved sibling evidence attached to child 1/);
  assert.match(sql,/Original location manager read another city child Documents/);
  assert.match(sql,/1-5 child family documents not isolated/);
  assert.match(sql,/^ROLLBACK;/m);
  assert.match(ci,/academy-enrollment-checks\.sql/);
  assert.match(scripts,/academy-enrollment-checks\.sql/);
});
