import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const panel=read('lib/admin/AdminGroupMatchDraftPanel.svelte');
const groups=read('routes/admin/groups/+page.svelte');
const workspace=read('lib/admin/AdminGroupWorkspace.svelte');
const territory=read('routes/admin/territories/+page.svelte');

test('a selected group owns the private match draft form, and standalone menu is not restored',()=>{
  assert.ok(compile(panel,{filename:'AdminGroupMatchDraftPanel.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(groups,{filename:'admin/groups/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(workspace,/import AdminGroupMatchDraftPanel/);
  assert.match(groups,/group=\{focusedGroup\}/);
  assert.match(workspace,/groupId=\{group\.id\}/);
  assert.match(workspace,/canWrite=\{access\.matchWrite\}/);
  assert.match(read('lib/admin/workspace-access.ts'),/matchWrite:matches&&canEdit&&has\('fixtures:write'\)/);
  assert.match(groups,/organization && \$auth\.session/);
  assert.match(groups,/data-testid="admin-group-workspace"/);
  for(const part of ['admin-group-new-match','admin-group-match-form','admin-group-private-drafts',
    'admin-group-matches-link','admin-group-training-link','admin-group-attendance-link']){
      assert.ok((groups+workspace+panel).includes(part),part);
  }
});

test('all match inputs derive from server-verified current group, city and locations',()=>{
  assert.match(panel,/directory\?\.groups\.find\(group=>group\.id===groupId\)/);
  assert.match(panel,/directory\?\.scopes\.find\(s=>s\.team_id===groupId\)/);
  assert.match(panel,/row\.scope_id===scope\?\.id&&row\.organization_id===organization/);
  assert.match(panel,/directory\?\.locations\.find\(l=>l\.id===assignment\?\.location_id\)/);
  assert.match(panel,/location\?\.can_manage/);
  assert.match(panel,/scope\.can_write_catalog/);
  assert.match(panel,/row\.academy_group_id===groupId:row\.club_team_id===groupId/);
  assert.match(panel,/opponents\.some\(x=>x\.id===opponentId\)/);
  assert.match(panel,/fields\.some\(x=>x\.id===fieldId\)/);
  assert.match(panel,/scope_id:scope\.id/);
  assert.match(panel,/location_id:assignment\?\.location_id,group_id:id/);
  assert.doesNotMatch(panel,/localStorage|sessionStorage|Math\.random/);
});

test('the real protected territory POST is called and only an exact confirmed draft is accepted',()=>{
  assert.match(panel,/method:'POST'/);
  assert.match(panel,/'X-Lefteria-Admin':'1'/);
  assert.match(panel,/kind:'match_draft'/);
  assert.match(panel,/credentials:'same-origin',cache:'no-store'/);
  assert.match(panel,/saved\.kind!=='match_draft'/);
  assert.match(panel,/saved\.organization_id!==owner/);
  assert.match(panel,/response\.status===401/);
  assert.match(panel,/response\.status===403/);
  assert.match(panel,/response\.status===409/);
  assert.match(panel,/role|allowed|mayWrite/);
  assert.match(panel,/Δεν έχει δημοσιευτεί|δημοσιευμένοι αγώνες|δημοσίευση/);
  assert.match(territory,/function submitMatch\(\)/);
  assert.match(territory,/void createResource\('match_draft'/);
});
