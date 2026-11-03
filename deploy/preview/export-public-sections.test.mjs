import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';
const exporter = createRequire(import.meta.url)('./export-source-manifests.js');
const clone = 'lefteria-os-preview-source-restore-20261031t212933z';
const id = 'e'.repeat(64);
const environment = { LEFTERIA_EXPORT_DATABASE: 'lefteria_fc', LEFTERIA_EXPORT_CLONE_NAME: clone,
  LEFTERIA_EXPORT_CLONE_ID: id, LEFTERIA_EXPORT_CLONE_MARKER: `${clone}:${id}`,
  LEFTERIA_EXPORT_BACKUP_SHA256: 'a'.repeat(64), LEFTERIA_EXPORT_SOURCE_COMMIT: exporter.SOURCE_COMMIT,
  LEFTERIA_EXPORT_TIME_ZONE: 'Asia/Nicosia', LEFTERIA_EXPORT_EXPORTED_AT: '2026-10-31T21:29:33Z',
  LEFTERIA_EXPORT_KIND: 'public-sections', LEFTERIA_EXPORT_BASE_PUBLIC_SHA256: 'b'.repeat(64), LEFTERIA_EXPORT_SNAPSHOT_VERSION: '1' };

test('public sections export requires explicit snapshot identity before any database read', async () => {
  let calls = 0;
  for (const changed of [{LEFTERIA_EXPORT_KIND:'other'}, {LEFTERIA_EXPORT_BASE_PUBLIC_SHA256:''},
    {LEFTERIA_EXPORT_SNAPSHOT_VERSION:'0'}, {LEFTERIA_EXPORT_SNAPSHOT_VERSION:'100001'}, {LEFTERIA_EXPORT_SNAPSHOT_VERSION:'1.5'}]) {
    await assert.rejects(exporter.exportFromMongo({getSiblingDB(){calls++;}}, {}, {...environment,...changed}), /source_manifest_export_refused/);
  }
  assert.equal(calls,0);
});

test('public sections export preserves all21 source families, private bytes and verified absent namespaces', () => {
  const configuration = exporter.readConfiguration(environment);
  const rows = Object.fromEntries(exporter.SECTION_COLLECTIONS.map(name => [name,[]]));
  rows.players = [{id:'player',private_medical:'source-evidence-sentinel',unknown:{deep:['preserved']}}];
  const result = exporter.buildSectionsBundle(configuration,rows,['reviews']);
  const entry = result.manifests[0]; const manifest = JSON.parse(entry.content);
  assert.equal(manifest.complete_collections.length,21);
  assert.deepEqual(manifest.complete_collections,exporter.SECTION_COLLECTIONS);
  assert.deepEqual(manifest.players,rows.players);
  assert.equal(manifest.base_public_sha256,'b'.repeat(64));
  assert.equal(entry.sha256,createHash('sha256').update(entry.content).digest('hex'));
  assert.equal(entry.bytes,Buffer.byteLength(entry.content));
  assert.deepEqual(result.provenance.absent_collections,['reviews']);
  assert.throws(()=>exporter.buildSectionsBundle(configuration,rows,['players']),/source_manifest_export_refused/);
  const incomplete={...rows};delete incomplete.staff;
  assert.throws(()=>exporter.buildSectionsBundle(configuration,incomplete),/source_manifest_export_refused/);
});
