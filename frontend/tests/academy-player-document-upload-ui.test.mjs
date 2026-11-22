import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const page=read('src/routes/admin/documents/+page.svelte');
const api=read('../backend/src/LefteriaFc.Api/Evaluations/AdminPrivateDocumentUploadEndpoints.cs');
const migration=read('../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110200000_PrivatePlayerDocumentQuarantine.cs');
const territory=read('../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115980000_PrivateDocumentTerritoryBoundary.cs');
const office=read('src/routes/admin/academy-office/+page.svelte');

test('selected player receives staff-only upload; public files and downloads remain disabled',()=>{
  assert.match(page,/data-testid="player-private-document-upload"/);
  assert.match(page,/canUpload=\$derived\(permitted/);
  assert.match(page,/academy:write/);
  assert.match(page,/teams:write/);
  assert.match(page,/new FormData\(\)/);
  assert.match(page,/\/api\/admin\/club\/players\//);
  assert.match(page,/encodeURIComponent\(player\)/);
  assert.match(page,/organization_id:owner/);
  assert.match(page,/uploadFile\.size>10\*1024\*1024/);
  assert.match(page,/crypto\.randomUUID\(\)/);
  assert.match(page,/data\.organization_id!==owner\|\|data\.player_id!==player/);
  assert.match(page,/data\.scan_status!=='quarantined'/);
  assert.doesNotMatch(page,/downloadUrl|publicMediaUrl|\/<img.+uploadFile/);
  assert.ok(compile(page,{filename:'admin/documents/+page.svelte',generate:'server'}).js.code.length>100);
});
test('approved Academy player deep-link selects the exact private player and manager scope',()=>{
  assert.match(office,/data-testid="approved-player-documents"/);
  assert.match(office,/row\.onboarded_player_id && canEnroll/);
  assert.match(office,/organization_id:'lefteria_fc_academy'/);
  assert.match(office,/player_id:row\.onboarded_player_id/);
  assert.match(page,/page\.url\.searchParams\.get\('player_id'\)/);
  assert.match(page,/owner!==organization/);
  assert.match(page,/!validId\(direct\)/);
});

test('backend encrypts and inserts into the actual player documents table, not a loose registration media store',()=>{
  assert.match(api,/MapPost\("\/\{playerId\}\/documents"/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/DocumentVault:KeyBase64/);
  assert.match(api,/RecognizeSafeMime\(plain\)/);
  assert.match(api,/new AesGcm\(key,16\)/);
  assert.match(api,/academy_private\.quarantine_admin_player_document/);
  assert.match(migration,/INSERT INTO academy_private\.player_documents\(/);
  assert.match(migration,/INSERT INTO academy_private\.encrypted_document_objects\(/);
  assert.match(migration,/FOREIGN KEY \(organization_id,player_id\)/);
  assert.match(territory,/CREATE TRIGGER tr_document_origin_capture/);
  assert.match(territory,/authorize_private_player_manager/);
});
