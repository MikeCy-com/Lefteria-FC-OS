import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const model=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110100000_AcademyPrivateEvaluationsDocuments.cs');
const evaluation=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110120000_AcademyEvaluationManagerRead.cs');
const documents=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110130000_AcademyDocumentMetadataRead.cs');
const evalApi=read('backend/src/LefteriaFc.Api/Evaluations/AdminAcademyEvaluationEndpoints.cs');
const docApi=read('backend/src/LefteriaFc.Api/Evaluations/AdminAcademyDocumentEndpoints.cs');
const evalUi=read('frontend/src/routes/admin/evaluations/+page.svelte');
const docUi=read('frontend/src/routes/admin/documents/+page.svelte');
const modules=read('frontend/src/lib/admin/module-catalog.ts');

test('the source-of-truth document and evaluation tables admit either organization',()=>{
  assert.equal((model.match(/CHECK \(organization_id IN \('lefteria_fc','lefteria_fc_academy'\)\)/g)||[]).length,2);
  assert.equal((model.match(/FOREIGN KEY \(organization_id,player_id\)/g)||[]).length,2);
  assert.doesNotMatch(model,/CREATE TABLE.*team_specific_evaluations|CREATE TABLE.*group_specific_documents/i);
});
test('both readers derive eligibility from current team membership, never a fixed team list',()=>{
  for(const source of [evaluation,documents]){
    assert.match(source,/_owner NOT IN \('lefteria_fc','lefteria_fc_academy'\)/);
    assert.match(source,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
    assert.match(source,/p\.is_active AND NOT p\.is_archived AND NOT p\.is_transferred/);
    assert.match(source,/_owner='lefteria_fc' AND p\.team_type='First Team'/);
    assert.match(source,/p\.team_id IS NOT NULL/);
    assert.match(source,/FROM club\.teams t/);
    assert.match(source,/t\.organization_id=_owner AND t\.id=p\.team_id/);
    assert.match(source,/_owner='lefteria_fc_academy' AND p\.team_type='Academy'/);
    assert.match(source,/p\.primary_academy_group_id/);
    assert.match(source,/FROM club\.player_academy_groups m/);
    assert.match(source,/JOIN club\.academy_groups g/);
    assert.doesNotMatch(source,/WHERE p\.team_id IN \(|hardcoded_team_id|staticTeamIds/);
  }
});
test('owner-scoped manager authorization applies on both API routes',()=>{
  for(const source of [evalApi,docApi]){
    assert.match(source,/organization_id is not \("lefteria_fc" or "lefteria_fc_academy"\)/);
    assert.match(source,/organization_id == "lefteria_fc" \? "teams:read" : "academy:read"/);
    assert.match(source,/session\.User\.Organizations\.Any\(o => o\.Id == organization_id\)/);
    assert.match(source,/\/api\/admin\/club\/players/);
    if(source===evalApi)assert.match(source,/MapPost/);
    else assert.doesNotMatch(source,/MapPost/);
    assert.doesNotMatch(source,/MapPut|MapDelete/);
  }
});
test('the admin UI follows current allowed organizations rather than a hardcoded Academy view',()=>{
  for(const source of [evalUi,docUi]){
    assert.match(source,/availableOrgs\.includes\(organization\)/);
    assert.match(source,/bind:value=\{organization\}/);
    assert.match(source,/organization_id:owner/);
    assert.match(source,/\/api\/admin\/club\/players\//);
  }
  assert.match(modules,/item\.id === 'evaluations'/);
  assert.match(modules,/item\.id === 'documents'/);
  assert.match(modules,/hasAdminPermission\(user\.permissions,'teams:read'\)/);
  assert.match(modules,/hasAdminPermission\(user\.permissions,'academy:read'\)/);
});