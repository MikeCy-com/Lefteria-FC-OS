import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync, existsSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const page=read('frontend/src/routes/admin/communications/+page.svelte');
const catalog=read('frontend/src/lib/admin/module-catalog.ts');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
const dashboard=read('frontend/src/lib/admin/AdminWorkspace.svelte');

test('communication centre compiles and is an app-only administration area',()=>{
  compile(page,{filename:'communications/+page.svelte',generate:'server'});
  assert.match(page,/data-testid="app-communications-centre"/);
  assert.match(page,/data-testid="communications-four-areas"/);
  for(const id of ['comms-announcements','comms-messages','comms-notifications','comms-attachments']){
    assert.match(page,new RegExp('id="'+id+'"'));
  }
  assert.match(page,/δεν δημοσιεύονται στον δημόσιο ιστότοπο/);
  assert.doesNotMatch(page,/@html|localStorage|sessionStorage|innerHTML|sendPush/);
});

test('only currently granted management may navigate, counts are organization scoped',()=>{
  assert.match(catalog,/id: 'communications'/);
  assert.match(catalog,/if \(item\.id === 'announcements' \|\| item\.id === 'communications'\)/);
  assert.match(page,/\['manager','super_admin'\]/);
  assert.match(page,/hasAdminPermission\(\$auth\.session!\.user\.permissions,'teams:write'\)/);
  assert.match(page,/hasAdminPermission\(\$auth\.session!\.user\.permissions,'academy:write'\)/);
  assert.match(page,/scopedAdminOrganization/);
  assert.match(page,/new URLSearchParams\(\{organization_id:owner\}\)/);
  assert.match(page,/credentials:'same-origin',cache:'no-store',signal:controller\.signal/);
  assert.match(page,/organization===owner&&actor===\$auth\.session\?\.user\.id/);
  assert.match(page,/response\.status===401|draftReply\.status===401/);
  assert.match(page,/draftReply\.status===403/);
  assert.match(page,/controller\.abort\(\)/);
  assert.doesNotMatch(page,/\/api\/mobile\/private\/conversations|\/api\/admin\/messages/);
});

test('the admin navigation links to the centre while preserving announcement editor',()=>{
  compile(frame,{filename:'AdminFrame.svelte',generate:'server'});
  compile(dashboard,{filename:'AdminWorkspace.svelte',generate:'server'});
  assert.match(frame,/data-testid="admin-communications-link"/);
  assert.match(frame,/data-testid="admin-announcements-link"/);
  assert.match(frame,/aria-current=\{active==='communications'/);
  assert.doesNotMatch(dashboard,/admin-quick-navigation|href:'\/admin\/communications'/);
  assert.match(page,/href="\/admin\/announcements"/);
  assert.doesNotMatch(page,/href="\/admin\/news"|href="\/admin\/leads"/);
});

test('private message and push admin writes are not falsely advertised as live',()=>{
  assert.match(page,/Διαχειριστικό API σε ανάπτυξη/g);
  assert.match(page,/Καμία προβολή ιδιωτικού ιστορικού τρίτων/);
  assert.match(page,/δεν έχουν ενεργοποιηθεί/);
  assert.match(catalog,/id: 'messages'[\s\S]*?status: 'planned'/);
  assert.match(catalog,/id: 'app_notifications'[\s\S]*?status: 'planned'/);
  assert.match(catalog,/id: 'app_media'[\s\S]*?status: 'planned'/);
});

test('every live communication navigation entry has the restored route file',()=>{
  assert.ok(existsSync(new URL('../src/routes/admin/communications/+page.svelte',import.meta.url)));
  assert.match(catalog,/id: 'communications'[\s\S]*?href: '\/admin\/communications'/);
  assert.match(frame,/href="\/admin\/communications"/);
  assert.match(dashboard,/href:'\/admin\/communications'/);
});
