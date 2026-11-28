import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const finance=read('frontend/src/routes/admin/finance/+page.svelte');
const groups=read('frontend/src/routes/admin/groups/+page.svelte');
const workspace=read('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
const fees=read('frontend/src/lib/admin/AdminTrainingFees.svelte');

test('per-player Academy billing links are restricted to finance readers and exact owner scope',()=>{
  for(const [name,source] of [['finance',finance],['groups',groups],['roster',roster],['fees',fees]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.match(groups,/AdminGroupWorkspace/);
  assert.match(workspace,/allowFinance=\{canFinance\}/);
  assert.match(workspace,/organization==='lefteria_fc_academy'&&/);
  assert.match(read('frontend/src/lib/admin/workspace-access.ts'),/finance:canEdit&&academy&&user\.role==='super_admin'&&has\('finance:read'\)/);
  assert.match(roster,/allowFinance && owner === 'lefteria_fc_academy'/);
  assert.match(roster,/data-testid="admin-player-billing-link"/);
  assert.match(roster,/encodeURIComponent\(player\.id\)/);
  assert.match(finance,/organizations\.find\(org => org\.id === requested\)/);
  assert.match(finance,/focusPlayerId=\{page\.url\.searchParams\.get\('player_id'\) \?\? ''\}/);
  assert.match(fees,/fees\.filter\(f=>f\.player_id===focus\)/);
  assert.match(fees,/data-testid="admin-fee-player-context"/);
  assert.match(fees,/\{#each visibleFees as fee \(fee\.id\)\}/);
  assert.match(fees,/Όλες οι χρεώσεις της Ακαδημίας/);
  assert.doesNotMatch(roster,/window\.open|localStorage/);
});
