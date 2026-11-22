import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const migration = readFileSync(new URL(
  '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261122190000_AcademyParentDocumentUploadCapability.cs',
  import.meta.url
), 'utf8');

// This suite is deliberately source-level: it guards the fail-closed SQL
// migration contract without requiring a database or enabling public intake.
test('anonymous upload SQL functions are private and use a fixed search path', () => {
  for (const name of ['submit_public_registration_with_upload', 'store_public_registration_document']) {
    const create = migration.indexOf('CREATE FUNCTION academy_intake.' + name);
    const revoke = migration.indexOf('REVOKE ALL ON FUNCTION\n        academy_intake.' + name);
    assert.ok(create >= 0, 'missing function ' + name);
    assert.ok(revoke > create, 'missing PUBLIC revoke for ' + name);
    const revokeStatement = migration.slice(revoke, migration.indexOf(';', revoke) + 1);
    assert.match(revokeStatement, /\)\s+FROM PUBLIC\s*;/, 'function revoke must target PUBLIC: ' + name);
    const definition = migration.slice(create, revoke);
    assert.match(definition, /SECURITY DEFINER\s+SET search_path=pg_catalog,pg_temp/);
    assert.doesNotMatch(definition, /\bGRANT\b/i);
  }
  assert.match(migration, /REVOKE ALL ON academy_intake\.registration_upload_capabilities FROM PUBLIC/);
  assert.match(migration, /ALTER TABLE academy_intake\.registration_upload_capabilities\s+ENABLE ROW LEVEL SECURITY/);
});

test('document write binds bearer to one pending child and locks capability row', () => {
  const store = migration.slice(migration.indexOf('CREATE FUNCTION academy_intake.store_public_registration_document('),
    migration.indexOf('REVOKE ALL ON FUNCTION\n        academy_intake.store_public_registration_document('));
  assert.match(store, /c\.registration_id=_registration/);
  assert.match(store, /c\.token_digest=_token_digest/);
  assert.match(store, /c\.expires_at>clock_timestamp\(\)/);
  assert.match(store, /r\.organization_id='lefteria_fc_academy'/);
  assert.match(store, /r\.status='pending'/);
  assert.match(store, /NOT EXISTS\(SELECT 1 FROM academy_intake\.registration_placements/);
  assert.match(store, /FOR UPDATE OF c/);
  assert.match(store, /private_attachment_unauthorized/);
  assert.match(store, /registration_document_slot_conflict/);
});

test('rollback refuses to discard issued upload capabilities', () => {
  const down = migration.slice(migration.indexOf('protected override void Down('));
  assert.match(down, /IF EXISTS\(SELECT 1 FROM academy_intake\.registration_upload_capabilities\)/);
  assert.match(down, /registration_upload_capabilities_need_forward_recovery/);
});
