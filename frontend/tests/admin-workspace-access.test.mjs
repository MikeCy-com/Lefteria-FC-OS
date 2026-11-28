import test from 'node:test';
import assert from 'node:assert/strict';
import {groupWorkspaceAccess} from '../src/lib/admin/workspace-access.ts';
import {readFileSync} from 'node:fs';
const academy='lefteria_fc_academy',first='lefteria_fc';
const user=(role,owner,permissions)=>({role,organizations:[{id:owner}],permissions});
test('training and attendance are separate and do not imply finance access',()=>{
  const access=groupWorkspaceAccess(user('manager',academy,['academy:write','training:read']),academy,true,true);
  assert.equal(access.training,true);
  assert.equal(access.trainingWrite,false);
  assert.equal(access.attendance,false);
  assert.equal(access.finance,false);
  assert.equal(access.players,true);
});
test('finance requires super admin and an exact academy grant',()=>{
  assert.equal(groupWorkspaceAccess(user('manager',academy,['*:*']),academy,true,true).finance,false);
  assert.equal(groupWorkspaceAccess(user('super_admin',academy,['*:*']),academy,true,true).finance,true);
  assert.equal(groupWorkspaceAccess(user('super_admin',first,['*:*']),academy,true,true).finance,false);
});
test('assigned first team manager can read the exact roster without creation or assignment tools',()=>{
  const access=groupWorkspaceAccess(user('manager',first,['teams:write','fixtures:read']),first,false,false);
  assert.equal(access.players,true);
  assert.equal(access.profileWrite,false);
  assert.equal(access.settings,false);
  assert.equal(access.matchWrite,false);
});
test('ungranted organization and roles do not mount restricted workspace tabs',()=>{
  for(const u of [user('manager',first,['*:*']),user('coach',academy,['*:*']),user('editor',academy,['*:*'])]){
    const access=groupWorkspaceAccess(u,academy,true,true);
    assert.ok(Object.values(access).every(v=>v===false));
  }
});
test('player records fetch only the current section and read-only rosters cannot enter assignment mode',()=>{
  const read=p=>readFileSync(new URL('../src/lib/admin/'+p,import.meta.url),'utf8');
  const records=read('AdminPlayerPrivateRecordsPanel.svelte');
  const roster=read('AdminRosterPlayersPanel.svelte');
  assert.ok(records.includes("currentSection==='documents'?['documents']:currentSection==='development'?['evaluations']"));
  assert.ok(records.includes('section===currentSection'));
  assert.ok(roster.includes("rosterMode==='group'||!allowAssignments"));
  assert.ok(roster.includes('if (!browser || !allowAssignments || saving'));
  assert.ok(roster.includes('focusedPlayer360?[focusedPlayer360]:visiblePlayers'));
});
