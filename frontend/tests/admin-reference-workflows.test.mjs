import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p, import.meta.url),'utf8');
const groups=read('frontend/src/routes/admin/groups/+page.svelte');
const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
const workspace=read('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
const shared=read('frontend/src/lib/admin/admin.css');
const shell=read('frontend/src/lib/admin/AdminFrame.svelte');

test('reference admin pages compile and preserve the real group and roster surfaces',()=>{
  for(const [name,source] of [['groups',groups],['roster',roster],['workspace',workspace]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.match(groups,/data-testid="admin-audiences-groups"/);
  assert.match(groups,/data-testid="admin-group-search"/);
  assert.match(groups,/data-testid="admin-group-workspace"/);
  assert.match(roster,/data-testid="admin-player-search"/);
  assert.match(roster,/data-testid="admin-player-individual-workspace"/);
  assert.match(roster,/group-players\?/);
});

test('search uses only authorization-filtered real data and does not pretend to search all pages',()=>{
  assert.match(groups,/directory\?\.groups/);
  assert.match(groups,/visibleGroups/);
  assert.match(roster,/\(page\?\.players\?\?\[\]\)/);
  assert.match(roster,/Η αναζήτηση αφορά μόνο τις εγγραφές της τρέχουσας σελίδας/);
  assert.match(roster,/const source=group\?/);
  assert.doesNotMatch(roster,/localStorage|sessionStorage/);
});

test('team workspace embeds the original actions, including coaches, behind existing grants',()=>{
  for(const id of ['players','training','attendance','matches','coaches','settings'])
    assert.match(workspace,new RegExp("tab==='"+id+"'"));
  assert.match(workspace,/requested==='coaches'&&canCreate/);
  assert.match(workspace,/AdminCoachDirectoryPanel/);
  assert.match(workspace,/AdminGroupAttendancePanel/);
  assert.match(workspace,/AdminGroupPublishedFixtures/);
  assert.match(workspace,/AdminAcademyGroupProfilePanel/);
  assert.match(workspace,/AdminRosterPlayersPanel/);
});

test('shared compact admin styles retain role-scoped nav without adding standalone player-data routes',()=>{
  assert.match(shared,/Lefteria FC reference admin system/);
  assert.match(shared,/\.admin-app \.admin-sidebar/);
  assert.match(shell,/adminSectionsFor\(user\)/);
  assert.match(shell,/liveIds/);
  assert.doesNotMatch(groups,/Math\.random|localStorage/);
});