import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/lib/admin/'+p,import.meta.url),'utf8');
const roster=read('AdminRosterPlayersPanel.svelte');
const records=read('AdminPlayerPrivateRecordsPanel.svelte');
test('Player 360 preserves one selected identity and the existing protected workflows',()=>{
  for(const [name,source] of [['roster',roster],['records',records]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.match(roster,/data-testid="admin-open-player-360"/);
  assert.match(roster,/data-testid="admin-player-360-workspace"/);
  assert.match(roster,/selectedPlayer360===player\.id/);
  assert.ok(roster.includes("{#key owner+':'+actorId+':'+player.id}"));
});
test('Player 360 section gates preserve owner, exact group and finance permissions',()=>{
  assert.ok(roster.includes("if(tab==='profile'&&!(allowPrivate&&owner==='lefteria_fc_academy'))return;"));
  assert.ok(roster.includes("allowPrivate&&owner==='lefteria_fc_academy'"));
  assert.ok(roster.includes("currentGroups(player).includes(currentGroup)"));
  assert.ok(roster.includes("allowFinance && owner === 'lefteria_fc_academy'"));
  assert.ok(roster.includes("section={player360Tab==='documents'?'documents':'development'}"));
  assert.match(records,/section!=='documents'/);
  assert.match(records,/section!=='development'/);
  assert.match(records,/admin-document-quarantine-form/);
  assert.match(records,/AdminPlayerDevelopmentGoals/);
});
