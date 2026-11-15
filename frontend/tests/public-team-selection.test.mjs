import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const root=new URL('../../',import.meta.url);
const read=p=>readFileSync(new URL(p,root),'utf8');
const page=read('frontend/src/routes/team/+page.svelte');
const loader=read('frontend/src/lib/server/public-sections.ts');

test('public team pages keep published team context across tabs and scoped records',()=>{
  assert.ok(compile(page,{filename:'routes/team/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(loader,/url\.searchParams\.get\('team_id'\)/);
  assert.match(loader,/teams\.some\(team => team\.id === requestedTeam\)/);
  assert.match(loader,/selectedTeamId: requestedTeam/);
  assert.match(page,/data-testid="public-team-selector"/);
  assert.match(page,/data-testid="public-team-select"/);
  assert.match(page,/aria-current=\{team\.id === data\.selectedTeamId/);
  assert.match(page,/matchesTeam\(item\.team_id\)/);
  assert.match(page,/matchesTeam\(row\.team_id\)/);
  assert.match(page,/row\.team_ids\.includes\(data\.selectedTeamId\)/);
  assert.match(page,/filter\(team => team\.id === data\.selectedTeamId\)/);
  assert.match(page,/withTeam\(id === 'overview'/);
  assert.match(page,/withTeam\('\/team\?tab=roster'\)/);
});
