import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const goal=read('lib/admin/AdminPlayerDevelopmentGoals.svelte');
const roster=read('lib/admin/AdminRosterPlayersPanel.svelte');
const group=read('lib/admin/AdminGroupWorkspace.svelte');
const privatePanel=read('lib/admin/AdminPlayerPrivateRecordsPanel.svelte');

test('Svelte compiles group player manager-only goals read UI without editor controls',()=>{
  for(const [name,source] of Object.entries({goal,roster,group,privatePanel}))
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>500,name);
  assert.match(group,/allowGoalRead=\{user\.role==='manager' && organization==='lefteria_fc_academy'\}/);
  assert.match(group,/allowGoalManagement=\{user\.role==='super_admin'\}/);
  assert.match(roster,/allowGoalRead && currentGroup && currentGroups\(player\)\.includes\(currentGroup\)/);
  assert.match(roster,/inspectedGoals===player\.id && allowGoalRead && currentGroup/);
  assert.match(roster,/readOnly \{onExpired\} \{onRevoked\}/);
  assert.doesNotMatch(roster,/localStorage|sessionStorage|\@html/);
});
test('read-only mode never offers mutation controls, including stale open dialogs',()=>{
  assert.match(goal,/\{#if !readOnly\}/);
  assert.match(goal,/\{#if showCreate&&!readOnly\}/);
  assert.match(goal,/\{#if goal\.status!=='archived'&&!readOnly\}/);
  assert.match(goal,/\{#if editing===goal\.id&&!readOnly\}/);
  assert.match(goal,/if\(!browser\|\|readOnly\|\|saving/);
  assert.match(goal,/if\(readOnly\|\|goal\.status==='archived'/);
  assert.match(goal,/if\(!browser\|\|readOnly\|\|saving\|\|loading\|\|editing!==goal\.id/);
  assert.match(goal,/readOnly===expectedReadOnly/);
  assert.match(goal,/if\(expectedReadOnly\)\{/);
  assert.match(goal,/credentials:'same-origin',cache:'no-store'/);
  assert.match(goal,/validPage\(raw,expectedOwner,expectedPlayer\)/);
  assert.match(privatePanel,/\{#if allowGoalManagement\}/);
});
