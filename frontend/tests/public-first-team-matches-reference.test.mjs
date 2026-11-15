import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const load=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const team=load('../src/routes/team/+page.svelte');
const table=load('../src/lib/public/PublicMatchScheduleTable.svelte');
const overview=load('../src/lib/public/PublicTeamMatchOverview.svelte');

test('matches reference hero, four action tabs and compact fixtures compile and retain existing team sections',()=>{
  for(const [name,s] of [['team/+page.svelte',team],['PublicMatchScheduleTable.svelte',table],['PublicTeamMatchOverview.svelte',overview]])
    assert.doesNotThrow(()=>compile(s,{filename:name,generate:'server'}),name);
  for(const marker of ['matches-reference-banner','matches-reference-tabs','match-cup-filter',
    'ΠΡΟΓΡΑΜΜΑΤΙΣΜΕΝΟΙ','ΑΠΟΤΕΛΕΣΜΑΤΑ','ΒΑΘΜΟΛΟΓΙΑ','ΚΥΠΕΛΛΟ',
    'PublicMatchScheduleTable','PublicTeamMatchOverview','public-team-selector',
    'class="public-subnav team-tabs"','gallery-grid','roster-filters','standings-anchor',
    'OrganizationSponsorStrip']) assert.ok(team.includes(marker),marker);
  assert.ok(team.indexOf('matches-reference-banner') < team.indexOf('team-section'));
  assert.match(team,/data\.tab === 'schedule' \|\| data\.tab === 'results'/);
  assert.match(team,/withTeam\('\/team\?tab=results'\)/);
  assert.match(team,/href=\{withTeam\('\/team#standings'\)\}/);
  assert.match(team,/organization_id === 'lefteria_fc'/);
  assert.doesNotMatch(team,/organization="lefteria_fc_academy"/);
});

test('cup choice and first team calendar derive only published scoped fixture data',()=>{
  assert.match(team,/item\.organization_id === 'lefteria_fc' && matchesTeam\(item\.team_id\)/);
  assert.match(team,/cupOnly/);
  assert.match(team,/item\.status === 'Completed'/);
  assert.match(table,/fixturePublicLink\(fixture\)/);
  assert.match(table,/fixtureStatus\(fixture\)/);
  assert.match(table,/fixture\.competition/);
  assert.match(table,/fixture\.match_date/);
  assert.match(table,/fixture\.venue/);
  assert.match(table,/fixture\.home_team_logo/);
  assert.match(table,/fixture\.away_team_logo/);
  assert.match(table,/bind:value=\{competition\}/);
  assert.match(table,/bind:value=\{season\}/);
  assert.doesNotMatch(table,/mock|placeholder.*fixture|Math\.random/);
});

test('all three source-style match panels remain visible without inventing standings or next match',()=>{
  for(const marker of ['team-featured-next-match','team-upcoming-match-list','team-first-team-standings',
    'Δεν υπάρχουν προγραμματισμένοι αγώνες','Δεν έχει δημοσιευτεί επιβεβαιωμένη βαθμολογία'])
    assert.ok(overview.includes(marker),marker);
  assert.match(overview,/\{#if table\.length\}/);
  assert.match(overview,/row\.won/);assert.match(overview,/row\.drawn/);
  assert.match(overview,/row\.lost/);assert.match(overview,/row\.points/);
  assert.doesNotMatch(overview,/fake|mocked|placeholder table/i);
});
