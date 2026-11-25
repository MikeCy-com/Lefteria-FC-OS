import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validDrill,validDrillPage,validDrillForm,canReadDrillLibrary} from '../src/lib/admin/central-academy-drills.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const row={id:'ef0a283c-0901-42f3-8b0f-c5324780a831',organization_id:'lefteria_fc_academy',
  title:'Passing',category:'technical',age_band:'u9-u11',duration_minutes:15,objective:'Accuracy',
  instructions:'First step\nSecond step',status:'approved',version:2};
const page={organization_id:row.organization_id,drills:[row],next_offset:null};
test('central catalogue is shared by Academy managers and coaches regardless of city or location',()=>{
  for(const role of ['super_admin','manager','coach'])assert.equal(canReadDrillLibrary(role,[{id:row.organization_id}]),true);
  for(const role of ['accountant','cashier','editor','marketing','player','parent','fan'])
    assert.equal(canReadDrillLibrary(role,[{id:row.organization_id}]),false);
  assert.equal(canReadDrillLibrary('coach',[{id:'lefteria_fc'}]),false);
});
test('approved catalogue projections reject private fields, foreign owners, malformed pagination and drafts',()=>{
  assert.equal(validDrill(row),true);
  assert.equal(validDrillForm(row),true);
  assert.equal(validDrillPage(page,'approved',0),true);
  for(const bad of [{...row,city_id:'city'},{...row,location_id:'site'},{...row,player_id:'private'},
    {...row,organization_id:'lefteria_fc'},{...row,duration_minutes:0},{...row,instructions:'bad\u0001text'}])
    assert.equal(validDrill(bad),false);
  assert.equal(validDrillPage({...page,drills:[{...row,status:'draft'}]},'approved',0),false);
  assert.equal(validDrillPage({...page,drills:[row,row]},'approved',0),false);
  assert.equal(validDrillPage({...page,next_offset:30},'approved',0),false);
  assert.equal(validDrillPage({...page,drills:new Array(1)},'approved',0),false);
});
test('central endpoints have no territorial selector and role-recheck database authorization',()=>{
  const migration=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125090000_CentralAcademyDrillLibrary.cs');
  const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyDrillEndpoints.cs');
  const grants=read('../../ops/postgres/central-academy-drills-admin-grants.sql');
  const sql=read('../../ops/postgres/central-academy-drills-checks.sql');
  assert.match(migration,/lock_admin_club_authority\(_digest,'lefteria_fc_academy'\)/);
  assert.match(migration,/actor_role NOT IN \('super_admin','manager','coach'\)/);
  assert.match(migration,/_write AND actor_role<>'super_admin'/);
  assert.match(migration,/v_role<>'super_admin' AND _status<>'approved'/);
  assert.match(migration,/FOR UPDATE/);
  assert.match(migration,/PRIMARY KEY\(drill_id,version\)/);
  assert.doesNotMatch(migration,/\b(?:city_id|location_id|group_id)\b/);
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/private, no-store/);
  assert.match(grants,/drill_table_exposed/);
  assert.match(sql,/cross_city_catalogue_differs/);
  assert.match(sql,/revoked_coach_catalogue_read_allowed/);
});
test('library and navigation compile without granting session or player access',()=>{
  const ui=read('../src/routes/admin/drills/+page.svelte');
  const frame=read('../src/lib/admin/AdminFrame.svelte');
  const workspace=read('../src/lib/admin/AdminGroupWorkspace.svelte');
  for(const [filename,source] of [['+page.svelte',ui],['AdminFrame.svelte',frame],['AdminGroupWorkspace.svelte',workspace]])
    for(const generate of ['client','server'])compile(source,{filename,generate});
  assert.match(ui,/if\(scope!==owner\|\|!canWrite\)return/);
  assert.match(ui,/validDrillPage\(value,proposals\?\(canWrite\?'draft':'all'\):st,o,proposals\)/);
  assert.match(ui,/if\(!browser\|\|!owner\|\|\(proposals&&!canSeeProposals\)\)return/);
  assert.match(ui,/st=canWrite\?status:'approved'/);
  assert.match(frame,/data-testid="admin-drills-link"/);
  assert.match(workspace,/href="\/admin\/drills"/);
});
