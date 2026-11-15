import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const groups=read('frontend/src/routes/admin/groups/+page.svelte');
const schedule=read('frontend/src/routes/admin/schedule/+page.svelte');
const attendance=read('frontend/src/routes/admin/attendance/+page.svelte');

test('management group CRUD permits a local Club/Academy choice for exactly granted organizations',()=>{
  assert.ok(compile(groups,{filename:'admin/groups/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(groups,/assignments\.length > 1/);
  assert.match(groups,/data-testid="admin-groups-organization"/);
  assert.match(groups,/if \(assignments\.some\(org => org\.id === next\)\) organization = next as Organization/);
  assert.match(groups,/data-testid="admin-group-create-form"/);
  assert.match(groups,/admin-groups-organization-choice/);
  assert.match(groups,/profile=selected==='lefteria_fc'/);
  assert.match(groups,/with-profile\?/);
  assert.match(groups,/Γυναικεία Ομάδα/);
  assert.match(groups,/Επιλεγμένη Ομάδα/);
  assert.doesNotMatch(groups,/localStorage|web_active_organization_id|admin-header-organization/);
});
test('per-Academy group action links carry exact server-validated group to training and attendance',()=>{
  for(const [name,source] of [['schedule',schedule],['attendance',attendance]])
    assert.ok(compile(source,{filename:'admin/'+name+'/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(groups,/group\.kind === 'team' \? 'ΟΜΑΔΑ' : 'ΤΜΗΜΑ ΑΚΑΔΗΜΙΑΣ'/);
  assert.match(groups,/organization === 'lefteria_fc_academy'/);
  const workspace=read('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
  assert.match(workspace,/admin-group-training-link/);
  assert.match(workspace,/admin-group-attendance-link/);
  assert.match(groups,/group=\{focusedGroup\}/);
  assert.match(workspace,/groupId=\{group\.id\}/);
  assert.match(groups,/focusedGroupId===group\.id/);
  assert.match(schedule,/page\.url\.searchParams\.get\('group_id'\)/);
  assert.match(schedule,/data\.groups\.find\(g=>g\.id===requestedGroup\)/);
  assert.match(schedule,/\/admin\/attendance\?group_id=/);
  assert.match(attendance,/page\.url\.searchParams\.get\('group_id'\)/);
  assert.match(attendance,/groups\.find\(g=>g\.id===requestedGroup\)/);
  assert.match(attendance,/if\(!validGroups\(payload\)\)/);
  assert.match(schedule,/if\(!isAcademyScheduleGroupList\(data\)\)/);
});
