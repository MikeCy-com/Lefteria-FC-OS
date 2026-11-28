import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/routes/admin/'+p+'/+page.svelte',import.meta.url),'utf8');
const groups=read('groups'),office=read('academy-office');
test('group and player workspace navigation follows changed route identity',()=>{
  assert.doesNotThrow(()=>compile(groups,{filename:'groups.svelte',generate:'server'}));
  assert.doesNotMatch(groups,/appliedRequestedView/);
  assert.ok(groups.includes('JSON.stringify([owner,actor,requestedView,requestedPlayer,requestedGroup])'));
  assert.ok(groups.includes("focusedGroupId=available.some(group=>group.id===requested)?requested:''"));
  assert.ok(groups.includes("if(!browser||!owner||!actor||!canCreate)"));
  assert.ok(groups.includes("owner==='lefteria_fc_academy'&&requestedPlayer!==null"));
});
test('successful enrollment offers both exact verified player and group links',()=>{
  assert.doesNotThrow(()=>compile(office,{filename:'academy-office.svelte',generate:'server'}));
  assert.ok(office.includes("result.group_id !== selectedGroup"));
  assert.ok(office.includes("justEnrolled={playerId:result.player_id,groupId:selectedGroup,actorId:actor}"));
  assert.ok(office.includes("justEnrolled.actorId===$auth.session.user.id"));
  assert.ok(office.includes("player_id:justEnrolled.playerId"));
  assert.ok(office.includes("group_id:justEnrolled.groupId,tab:'players'"));
  assert.ok(office.includes("if ($auth.session?.user.id !== actor || !actor || !canEnroll || tab!=='registrations') return;"));
});
