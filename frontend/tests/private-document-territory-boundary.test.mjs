import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115980000_PrivateDocumentTerritoryBoundary.cs');
const fixture=load('ops/postgres/football-territory-delegation-checks.sql');
const runner=load('.github/workflows/rewrite-build.yml');
test('encrypted document metadata retains immutable creation-time origin',()=>{
  for(const clause of [
    'origin_scope_id','origin_city_id','origin_location_id',
    'origin_recorded_at','ck_document_origin_complete',
    'tr_document_origin_capture','tr_document_origin_immutable',
    'document_origin_server_only','document_origin_immutable',
    'FOREIGN KEY (origin_city_id,origin_location_id)'
  ])assert.ok(migration.includes(clause),clause);
  assert.match(migration,/NEW\.origin_recorded_at:=clock_timestamp\(\)/);
  assert.match(migration,/REVOKE ALL ON FUNCTION academy_private\.capture_document_origin\(\) FROM PUBLIC/);
});
test('reader, audit events and encrypted uploader all enforce exact manager scope',()=>{
  assert.match(migration,/authorize_private_player_manager\(/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION academy_private\.read_admin_player_document_metadata/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION academy_private\.quarantine_admin_player_document/);
  assert.equal((migration.match(/d\.origin_scope_id=v_scope/g)||[]).length,3,
    'count, per-document audit and response list must filter identically');
  for(const clause of ['v_role=\'super_admin\' OR','d.origin_city_id=v_city',
    'd.origin_location_id=v_location','PERFORM 1 FROM academy_private.authorize_private_player_manager'])
    assert.ok(migration.includes(clause),clause);
  assert.doesNotMatch(migration,/GRANT EXECUTE|UPDATE academy_private\.player_documents SET origin_/);
});
test('disposable PostgreSQL fixtures test write, read, sibling and transfer security',()=>{
  for(const id of [
    'document_manager_upload_unverified','document_manager_read_own_site_failed',
    'manager_uploaded_sibling_private_document','manager_read_sibling_private_document',
    'city_manager_cannot_read_site_document','document_origin_not_recorded',
    'document_origin_was_mutable','old_city_manager_read_transferred_document',
    'new_city_manager_inherited_old_document'
  ])assert.ok(fixture.includes(id),id);
  assert.match(runner,/football-territory-delegation-checks\.sql/);
  assert.match(fixture,/BEGIN;[\s\S]*ROLLBACK;/);
});
