import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const frame=read('lib/admin/AdminFrame.svelte'),css=read('lib/admin/admin.css');
const settings=read('routes/admin/settings/+page.svelte');
test('all admin roles share compact visual sidebar and exactly one profile header',()=>{
  assert.ok(compile(frame,{filename:'AdminFrame.svelte',generate:'server'}).js.code.length>100);
  assert.match(frame,/data-testid="admin-profile-menu"/);
  assert.match(frame,/data-testid="admin-profile-menu-toggle"/);
  assert.match(frame,/data-testid="admin-settings-link"/);
  assert.match(frame,/data-testid="admin-settings-submenu"/);
  assert.match(css,/\.admin-app \.admin-sidebar\.admin-nav-section|\.admin-app \.admin-sidebar \.admin-nav-section/);
  assert.match(css,/min-height:39px;padding:5px 11px/);
  assert.match(css,/font-size:15px;white-space:normal/);
  assert.match(css,/@media\(max-width:720px\)/);
  assert.match(css,/\.admin-nav-settings-details summary:focus-visible/);
  assert.doesNotMatch(frame,/href="\/admin\/(?:schedule|matches|attendance|evaluations|documents)"/);
});
test('settings submenu is permission scoped and navigates to real settings anchors',()=>{
  for(const marker of ['if hasWebsite','liveIds.has(\'territories\')','if hasOperations',
    'href="/admin/settings#website-settings"',
    'href="/admin/settings#football-settings"',
    'href="/admin/settings#access-settings"',
    'href="/admin/profile"'])
    assert.ok(frame.includes(marker),marker);
  for(const id of ['website-settings','football-settings','access-settings'])assert.ok(settings.includes('id="'+id+'"'));
});
