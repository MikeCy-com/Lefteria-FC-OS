import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const frame=read('lib/admin/AdminFrame.svelte'),css=read('lib/admin/admin.css');
const settings=read('routes/admin/settings/+page.svelte');

test('all admin roles share compact one-level navigation and one account header',()=>{
  assert.ok(compile(frame,{filename:'AdminFrame.svelte',generate:'server'}).js.code.length>100);
  assert.match(frame,/data-testid="admin-profile-menu"/);
  assert.match(frame,/data-testid="admin-profile-menu-toggle"/);
  assert.match(frame,/data-testid="admin-settings-link"/);
  assert.match(frame,/data-testid="admin-football-navigation"/);
  assert.match(frame,/data-testid="admin-website-navigation"/);
  assert.match(frame,/data-testid="admin-member-app-navigation"/);
  assert.match(frame,/data-testid="admin-crm-navigation"/);
  assert.match(frame,/data-testid="admin-commerce-navigation"/);
  assert.equal((frame.match(/class="admin-nav-disclosure"/g)||[]).length,5);
  assert.doesNotMatch(frame,/data-testid="admin-settings-submenu"/);
  assert.doesNotMatch(frame,/class="admin-nav-settings-details"/);
  assert.match(css,/\.admin-app \.admin-sidebar \.admin-nav-category/);
  assert.match(css,/font:800 18px\/1\.28 'Fira Sans'/);
  assert.match(css,/font:600 15px\/1\.35 'Fira Sans'/);
  assert.match(css,/@media\(max-width:720px\)/);
  assert.match(css,/\.admin-app \.admin-sidebar \.admin-nav-category:focus-visible/);
  assert.match(frame,/\{#if liveIds\.has\('assigned_groups'\)\}\s*<a[^>]+href="\/admin\/schedule"[^>]+data-testid="admin-coach-workspace-link"/);
  assert.equal((frame.match(/href="\/admin\/schedule"/g)||[]).length,1);
  assert.doesNotMatch(frame,/href="\/admin\/(?:matches|attendance|evaluations|documents)"/);
});

test('settings is one entry point with grant-scoped real tools and no second nested submenu',()=>{
  assert.ok(compile(settings,{filename:'settings/+page.svelte',generate:'server'}).js.code.length>100);
  assert.equal((frame.match(/href="\/admin\/settings"/g)||[]).length,2,
    'One sidebar Settings destination plus one account menu destination');
  assert.match(settings,/adminSectionsFor\(\$auth\.session\.user\)/);
  assert.match(settings,/module\.id==='onboarding'&&module\.status==='live'/);
  assert.match(settings,/data-testid="admin-settings-guide"/);
  assert.match(settings,/href="\/admin\/onboarding"/);
  for(const id of ['website-settings','football-settings','access-settings','organization-settings'])
    assert.ok(settings.includes('id="'+id+'"'),id);
  assert.match(settings,/data-testid="admin-organization-settings"/);
  assert.match(settings,/org\.id==='lefteria_fc'/);
  assert.match(settings,/href="\/admin\/territories"/);
  assert.match(settings,/href="\/admin\/staff"/);
  assert.match(settings,/href="\/admin\/users"/);
  assert.match(settings,/Ο Super Admin μπορεί να αλλάζει ρόλο/);
  assert.doesNotMatch(settings,/Η αλλαγή ρόλου δεν υποστηρίζεται ακόμη/);
});
