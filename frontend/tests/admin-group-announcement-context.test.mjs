import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const group=read('lib/admin/AdminGroupWorkspace.svelte');
const notices=read('routes/admin/announcements/+page.svelte');

test('group workspace opens app announcements for its real selected group',()=>{
  compile(group,{filename:'AdminGroupWorkspace.svelte',generate:'server'});
  assert.match(group,/data-testid="admin-group-app-announcements-link"/);
  assert.match(group,/href=\{'\/admin\/announcements\?organization_id='\+encodeURIComponent\(organization\)/);
  assert.match(group,/encodeURIComponent\(group\.id\)/);
  assert.match(group,/const canAnnounce=\$derived\(access\.announcements\)/);
  assert.match(read('lib/admin/workspace-access.ts'),/announcements:canEdit&&has\('messages:write'\)/);
  assert.match(group,/\{#if canAnnounce\}[\s\S]*?data-testid="admin-group-app-announcements-link"/);
  assert.doesNotMatch(group,/\/api\/mobile\/private|\/api\/admin\/messages/);
});

test('announcement deep links are accepted only for a fresh canonical audience group',()=>{
  compile(notices,{filename:'admin/announcements/+page.svelte',generate:'server'});
  assert.match(notices,/import \{ page \} from '\$app\/state'/);
  assert.match(notices,/permitted\.some\(org=>org\.id===requested\)/);
  assert.match(notices,/const requestedGroup=page\.url\.searchParams\.get\('group_id'\)/);
  assert.match(notices,/requestedOwner===owner && requestedGroup &&/);
  assert.match(notices,/groupRaw\.groups\.some\(g=>g\.id===requestedGroup\)/);
  assert.match(notices,/groupId=requestedGroup/);
  assert.match(notices,/originGroupId=requestedGroup/);
  assert.match(notices,/data-testid="announcement-return-to-group"/);
  assert.match(notices,/data-testid="announcement-verified-target"/);
  assert.match(notices,/controller\.abort\(\)/);
  assert.doesNotMatch(notices,/localStorage|sessionStorage|@html/);
});

test('group-deep-link never bypasses existing server-authorized publication',()=>{
  assert.match(notices,/\/api\/admin\/football\/audiences/);
  assert.match(notices,/\/api\/admin\/communications\/announcement-drafts/);
  assert.match(notices,/\/api\/admin\/communications\/app-announcements/);
  assert.match(notices,/hasAdminPermission\(\$auth\.session!\.user\.permissions,'teams:write'\)/);
  assert.match(notices,/hasAdminPermission\(\$auth\.session!\.user\.permissions,'academy:write'\)/);
  assert.match(notices,/window\.confirm\(/);
  assert.match(notices,/method:'POST',credentials:'same-origin',cache:'no-store'/);
});
