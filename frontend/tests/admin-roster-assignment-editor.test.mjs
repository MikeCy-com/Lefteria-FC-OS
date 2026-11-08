import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const base=new URL('../../',import.meta.url);
const read=p=>readFileSync(new URL(p,base),'utf8');
const panel=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
const route=read('frontend/src/routes/admin/groups/+page.svelte');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108140000_AdminRosterAssignment.cs');
test('assignment editor saves only canonical group IDs with expected membership and refreshes directory',()=>{
  assert.match(panel,/admin-player-assignment-editor/);
  assert.match(panel,/method:'PUT',credentials:'same-origin',cache:'no-store'/);
  assert.match(panel,/'X-Lefteria-Admin':'1'/);
  assert.match(panel,/expected_group_ids:previous,group_ids:desired/);
  assert.match(panel,/response.status===409/);
  assert.match(panel,/response.status===401/);
  assert.match(panel,/response.status===403/);
  assert.match(panel,/onChanged\(\)/);
  assert.match(route,/onChanged=\{\(\) => retry\+\+\}/);
  assert.doesNotMatch(panel,/@html|innerHTML|\/api\/admin\/messages/);
});
test('single FC team and bounded plural Academy group UI',()=>{
  assert.match(panel,/owner==='lefteria_fc'/);
  assert.match(panel,/selectedGroups.length>=20/);
  assert.match(panel,/selectedGroups.length>1/);
  assert.match(sql,/IF v_current<>v_expected THEN/);
  assert.match(sql,/DELETE FROM club\.player_academy_groups/);
});
