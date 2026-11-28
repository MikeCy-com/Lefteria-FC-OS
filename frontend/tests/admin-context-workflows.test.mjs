import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const frame=read('lib/admin/AdminFrame.svelte');
const dashboard=read('lib/admin/AdminWorkspace.svelte');
const groups=read('routes/admin/groups/+page.svelte');
const users=read('routes/admin/users/+page.svelte');
const territories=read('routes/admin/territories/+page.svelte');
const workspace=read('lib/admin/AdminGroupWorkspace.svelte');
const catalog=read('lib/admin/module-catalog.ts');
const roster=read('lib/admin/AdminRosterPlayersPanel.svelte');
const delegation=read('lib/admin/FootballTerritoryAccessPanel.svelte');
const attendance=read('lib/admin/AdminGroupAttendancePanel.svelte');
const profile=read('routes/admin/profile/+page.svelte');
const settings=read('routes/admin/settings/+page.svelte');

test('all shared admin routes and contextual player records compile in Svelte',()=>{
  for(const [name,source] of Object.entries({frame,dashboard,groups,users,territories,workspace,roster,attendance,delegation,profile,settings}))
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100,name);
});

test('global navigation has no standalone player or match tools; profile and settings apply to every role',()=>{
  for(const path of ['schedule','matches','attendance','evaluations','documents'])
    assert.doesNotMatch(dashboard,new RegExp('href="/admin/'+path+'"'));
  for(const path of ['matches','evaluations','documents'])
    assert.doesNotMatch(frame,new RegExp('href="/admin/'+path+'"'));
  const assigned=frame.match(/\{#if academyCoachTools\}([\s\S]*?)\{\/if\}/)?.[1]??'';
  assert.match(assigned,/href="\/admin\/schedule"/);
  assert.match(assigned,/href="\/admin\/attendance"/);
  assert.match(frame,/data-testid="admin-profile-menu-toggle"/);
  assert.match(frame,/href="\/admin\/profile"/);
  assert.match(frame,/href="\/admin\/settings"/);
  assert.match(frame,/data-testid="admin-settings-link"/);
  assert.match(frame,/class="admin-nav-disclosure"/);
  assert.doesNotMatch(frame,/data-testid="admin-settings-submenu"/);
  assert.match(settings,/data-testid="admin-settings-guide"/);
  assert.doesNotMatch(frame,/\{#if hasWebsite \|\| liveIds\.has\('territories'\) \|\| hasOperations\}/);
  assert.match(profile,/data-testid="admin-self-password-form"/);
  assert.match(settings,/id="website-settings"/);
});

test('creation shortcuts live in their module forms and not on the dashboard',()=>{
  for(const marker of ['admin-creation-actions','admin-quick-navigation','Γρήγορη δημιουργία','Οι εργασίες μου'])
    assert.ok(!dashboard.includes(marker),marker);
  assert.match(dashboard,/AdminClubMetrics/);
  assert.match(dashboard,/data-testid="admin-dashboard-refresh"/);
  assert.match(groups,/page\.url\.searchParams\.get\('new'\)/);
  assert.match(groups,/choices\.some\(item=>item\.id===requested\)/);
  assert.match(groups,/if\(!browser\|\|openedRequestedCreate\|\|!wantsNew\|\|!loaded\|\|!canCreate\)return/);
  assert.match(groups,/\/api\/admin\/football\/groups\/with-profile/);
  assert.match(territories,/id="create-city"/);
  assert.match(territories,/id="create-location"/);
  assert.match(users,/routePage\.url\.searchParams\.get\('new'\)/);
  assert.match(users,/permitted/);
  assert.match(users,/\/api\/admin\/access\/users/);
  assert.match(users,/data-testid="admin-new-account-scope-link"/);
  assert.match(territories,/id="delegated-access"/);
  assert.match(territories,/focusAccountId=\{page\.url\.searchParams\.get\('account_id'\)/);
  assert.match(delegation,/access\.accounts\.some\(item=>item\.id===focusAccountId\)/);
  for(const id of ['academy_schedule','attendance','documents','evaluations','fixtures'])
    assert.doesNotMatch(catalog,new RegExp("id: '"+id+"'"));
});

test('athlete attendance is accessible only within Academy group player file',()=>{
  assert.match(workspace,/canReadAttendance=\{access\.attendance\}/);
  assert.match(workspace,/canWriteAttendance=\{canAttendanceWrite\}/);
  assert.match(roster,/owner==='lefteria_fc_academy' && canReadAttendance/);
  assert.match(roster,/currentGroup && owner==='lefteria_fc_academy'/);
  assert.match(roster,/playerId=\{player\.id\}/);
  assert.match(roster,/data-testid="admin-player-attendance-history"/);
  assert.match(roster,/AdminPlayerPrivateRecordsPanel/);
  assert.match(attendance,/playerId=null,playerName=''/);
  assert.match(attendance,/register\.players\.filter\(row=>/);
  assert.match(attendance,/row\.player_id===playerId/);
  assert.match(attendance,/\{#each visiblePlayers as player/);
  assert.match(attendance,/\/api\/admin\/academy\/training/);
  assert.match(attendance,/expected_version:version/);
  assert.match(workspace,/AdminGroupMatchDraftPanel/);
  assert.match(workspace,/AdminGroupPublishedFixtures/);
});
