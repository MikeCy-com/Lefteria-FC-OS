import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const frame=read('lib/admin/AdminFrame.svelte');
const home=read('lib/admin/AdminWorkspace.svelte');
const settings=read('routes/admin/settings/+page.svelte');
const profile=read('routes/admin/profile/+page.svelte');
const groups=read('routes/admin/groups/+page.svelte');
const groupWorkspace=read('lib/admin/AdminGroupWorkspace.svelte');
const matches=read('routes/admin/matches/+page.svelte');

test('new sidebar, compact dashboard, settings and private profile compile as genuine Svelte routes',()=>{
  for(const [name,source] of Object.entries({frame,home,settings,profile,groups,matches})){
    assert.doesNotThrow(()=>compile(source,{filename:name+'.svelte',generate:'server'}),name);
  }
});

test('top-right account entry owns profile and session, without dashboard clutter',()=>{
  assert.match(frame,/data-testid="admin-profile-menu"/);
  assert.match(frame,/data-testid="admin-profile-menu-toggle"/);
  assert.match(frame,/href="\/admin\/profile"/);
  assert.match(frame,/href="\/admin\/settings"/);
  assert.match(frame,/data-testid="admin-logout"/);
  assert.match(profile,/data-testid="admin-my-profile"/);
  assert.match(profile,/id="security"/);
  assert.match(profile,/roleName/);
  assert.doesNotMatch(home,/id="account"|id="session"|id="access"|admin-overview-stats/);
  assert.doesNotMatch(home,/plannedCount|liveCount|Under Developments/);
  assert.match(home,/data-testid="admin-dashboard-metrics"/);
  assert.doesNotMatch(home,/admin-creation-shortcuts|admin-quick-navigation|Οι εργασίες μου/);
  assert.match(home,/adminOrganizationsFor\(user\)/);
});

test('five group-only tools disappear from both global navigation and dashboard',()=>{
  for(const slug of ['schedule','attendance','matches','evaluations','documents']){
    assert.doesNotMatch(frame,new RegExp('href="/admin/'+slug+'"'));
    assert.doesNotMatch(home,new RegExp('href="/admin/'+slug+'"'));
  }
  assert.match(groups,/data-testid="admin-group-workspace"/);
  assert.match(groups,/focusedGroupId/);
  assert.match(groups,/group\.id===focusedGroupId|focusedGroupId===group\.id/);
  for(const marker of ['admin-group-training-link','admin-group-attendance-link','admin-group-matches-link',
    'admin-group-players-link','AdminRosterPlayersPanel','AdminAcademyGroupProfilePanel','AdminFirstTeamProfilePanel'])
    assert.ok((groups+groupWorkspace).includes(marker),marker);
  assert.match(matches,/requestedOrganization/);
  assert.match(matches,/requestedGroup/);
  assert.match(matches,/choices\.some\(item=>item\.id===requestedGroup\)/);
});

test('settings are capability-scoped and profile writes require verified self-service API',()=>{
  assert.match(settings,/hasAdminPermission\(\$auth\.session\.user\.permissions/);
  assert.match(settings,/data-testid="admin-settings-home"/);
  assert.match(settings,/data-testid="admin-settings-club"/);
  assert.match(settings,/data-testid="admin-settings-territories"/);
  assert.match(settings,/canStaff \|\| canUsers/);
  assert.match(profile,/data-testid="admin-self-name-form"/);
  assert.match(profile,/data-testid="admin-self-password-form"/);
  assert.match(profile,/Η αλλαγή κωδικού απαιτεί/);
  assert.match(profile,/method:'POST',credentials:'same-origin',cache:'no-store'/);
  assert.match(profile,/response\.status!==204/);
  assert.match(profile,/auth\.invalidateSession\(\)/);
  assert.match(profile,/email απαιτεί επαλήθευση/);
  assert.doesNotMatch(profile,/localStorage|sessionStorage|oninput=.*fetch/);
});

test('first-team and Academy group creation remain permission-gated in their own module',()=>{
  assert.match(groups,/organization !== 'lefteria_fc' \|\| \$auth\.session\.user\.role==='super_admin'/);
  assert.match(groups,/data-testid="admin-group-create"/);
  assert.match(groups,/data-testid="admin-group-verified-location"/);
  assert.doesNotMatch(home,/new=1|create-city|create-location/);
});
