import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/routes/admin/'+p,import.meta.url),'utf8');
const group=read('groups/+page.svelte');
const territory=read('territories/+page.svelte');

test('group and territory city/location wizard endpoints compile',()=>{
  for(const [name,s] of [['admin/groups/+page.svelte',group],['admin/territories/+page.svelte',territory]])
    assert.ok(compile(s,{filename:name,generate:'server'}).js.code.length>100,name);
});

test('new Academy group is assigned to a verified site in the same saved transaction',()=>{
  assert.match(group,/lastCreatedGroupId=value\.id/);
  assert.match(group,/value\.kind !==/);
  assert.match(group,/value\.location_id!==selectedLocation\.id/);
  assert.match(group,/data-testid="admin-group-next-setup"/);
  assert.match(group,/data-testid="admin-verified-group-location"/);
  assert.match(group,/\/admin\/territories\?organization_id=lefteria_fc_academy&city_id=/);
  assert.doesNotMatch(group,/data-testid="admin-assign-group-location"/);
  assert.match(group,/data-testid="admin-assign-team-scope"/);
  assert.match(group,/\/admin\/territories\?organization_id=lefteria_fc&team_id=/);
  assert.match(territory,/data-testid="territory-group-placement"/);
  assert.match(territory,/void createResource\('group_assignment'/);
  assert.match(territory,/if\(action==='group_assignment'/);
  assert.match(territory,/placedGroupId=data\.group_id/);
  assert.match(territory,/data-testid="territory-return-to-created-group"/);
});

test('ownership boundary stays server-verified and city-level match catalogs are unchanged',()=>{
  assert.match(territory,/\.user\.organizations \?\? \[\]/);
  assert.match(territory,/owners\.includes\(organization\)/);
  assert.match(territory,/canWrite = \$derived/);
  assert.match(territory,/mayManageLocation && directory\.unassigned_groups\.length>0/);
  assert.match(territory,/selectedScope && selectedGroup/);
  assert.match(territory,/scope_id:selectedScope\.id,location_id:locationId,group_id:selectedGroup/);
  assert.match(territory,/if\(actor!==\$auth\.session\?\.user\.id \|\| organization!==owner\) return/);
  assert.match(territory,/response\.status===409/);
  assert.match(territory,/result as Record<string,unknown>\)\.organization_id!==owner/);
  assert.match(territory,/organization==='lefteria_fc_academy'/);
  assert.match(territory,/Οι αντίπαλοι και τα γήπεδα αγώνων της Ακαδημίας ανήκουν στην πόλη/);
  assert.match(territory,/canManageCity=/);
  assert.doesNotMatch(territory,/localStorage|sessionStorage|Math\.random/);
});
