import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const home=read('lib/admin/AdminWorkspace.svelte');
const frame=read('lib/admin/AdminFrame.svelte');
const settings=read('routes/admin/settings/+page.svelte');
const groups=read('routes/admin/groups/+page.svelte');
const territories=read('routes/admin/territories/+page.svelte');
const users=read('routes/admin/users/+page.svelte');

test('overview contains verified metrics, not duplicate admin navigation and creation',()=>{
  assert.ok(compile(home,{filename:'AdminWorkspace.svelte',generate:'server'}).js.code.length>100);
  assert.match(home,/AdminClubMetrics/);
  assert.match(home,/data-testid="admin-dashboard-refresh"/);
  for(const text of ['Επιλέξτε πού θέλετε να εργαστείτε','Γρήγορη δημιουργία',
    'Οι εργασίες μου','admin-action-grid','admin-creation-shortcut-grid',
    'admin-quick-navigation','admin-creation-actions','ΚΕΝΤΡΟ ΔΙΑΧΕΙΡΙΣΗΣ'])
    assert.ok(!home.includes(text),text);
  assert.doesNotMatch(home,/href="\/admin\/(?:groups|territories|users|news|finance|pos)"/);
});

test('authorized admin links and creation controls remain in their actual modules',()=>{
  for(const [name,source] of Object.entries({frame,settings,groups,territories,users}))
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100,name);
  for(const href of ['/admin/groups','/admin/territories','/admin/news',
    '/admin/website-pages','/admin/academy-office','/admin/finance','/admin/pos',
    '/admin/leads','/admin/hub','/admin/communications','/admin/settings'])
    assert.ok(frame.includes('href="'+href+'"')||(['/admin/groups','/admin/territories'].includes(href)&&['lefteria_fc','lefteria_fc_academy'].every(owner=>frame.includes('href="'+href+'?organization_id='+owner+'"'))),href);
  assert.match(groups,/data-testid="admin-group-create-form"/);
  assert.match(territories,/id="create-city"/);
  assert.match(territories,/id="create-location"/);
  assert.match(settings,/data-testid="admin-settings-new-user"/);
  assert.match(settings,/href="\/admin\/users\?new=1"/);
  assert.match(users,/routePage\.url\.searchParams\.get\('new'\)/);
  assert.match(frame,/adminSectionsFor\(user\)/);
});

test('repeated generic Lefteria FC eyebrow is removed from admin header and affected pages',()=>{
  assert.doesNotMatch(frame,/ΚΕΝΤΡΟ ΔΙΑΧΕΙΡΙΣΗΣ/);
  assert.match(frame,/ΛΕΥΤΕΡΙΑ FC <small>CMS<\/small>/);
  for(const path of ['routes/admin/settings/+page.svelte',
    'routes/admin/leads/+page.svelte','routes/admin/matches/+page.svelte',
    'routes/admin/schedule/+page.svelte','routes/admin/territories/+page.svelte',
    'routes/admin/hub/+page.svelte','routes/admin/hub/player-quizzes/+page.svelte']){
    const page=read(path);
    assert.doesNotMatch(page,/<p class="(?:admin-)?eyebrow">(?:ΛΕΥΤΕΡΙΑ FC|[^<]* · ΛΕΥΤΕΡΙΑ FC)/,path);
  }
});
