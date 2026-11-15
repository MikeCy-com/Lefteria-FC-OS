import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const matches=read('lib/public/PublicTeamMatchOverview.svelte');
const team=read('routes/team/+page.svelte');
const server=read('lib/server/public-sections.ts');
const fixtures=read('lib/public/Fixtures.svelte');

test('first team match overview presents reference next game, upcoming and standings panels',()=>{
  for(const [name,source] of [['PublicTeamMatchOverview',matches],['team/+page',team]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  for(const id of ['team-matches-overview','team-featured-next-match','team-upcoming-match-list','team-first-team-standings'])
    assert.ok(matches.includes('data-testid="'+id+'"'));
  assert.ok(matches.includes('grid-template-columns:minmax(0,1.2fr) minmax(0,1fr) minmax(0,.9fr)'));
  assert.ok(matches.includes('sort((a,b)=>a.match_date.localeCompare(b.match_date)'));
  assert.ok(matches.includes('nearest=$derived(upcoming.slice(0,4))'));
  assert.ok(matches.includes('href={\'/match/\'+encodeURIComponent(next.id)}'));
  assert.ok(matches.includes('@media(max-width:780px)'));
});
test('selected team scope stays on all match pages and individual fixtures',()=>{
  assert.ok(team.includes('item.organization_id === \'lefteria_fc\' && matchesTeam(item.team_id)'));
  assert.ok(team.includes('<PublicTeamMatchOverview {fixtures} standings={firstTeamStandings}'));
  assert.ok(team.includes('firstTeamStandings = $derived(selectedTeam &&'));
  assert.ok(team.includes("row.organization_id==='lefteria_fc'"));
  assert.ok(matches.includes('teamId?'));
  assert.ok(matches.includes("encodeURIComponent(teamId)"));
  assert.ok(server.includes("if (requestedTeam && !teams.some(team => team.id === requestedTeam))"));
  assert.ok(fixtures.includes('href={`/match/${encodeURIComponent(fixture.id)}`}'));
  assert.ok(team.includes('<Fixtures {fixtures} />'),'full schedule remains below overview');
  assert.ok(!matches.includes('lefteria_fc_academy'));
});
