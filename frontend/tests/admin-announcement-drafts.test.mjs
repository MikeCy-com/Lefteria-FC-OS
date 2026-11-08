import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108190000_AdminAnnouncementDrafts.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminAnnouncementDraftEndpoints.cs');
const ui=read('frontend/src/routes/admin/announcements/+page.svelte');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-announcement-drafts-boundary.sql');
const nav=read('frontend/src/lib/admin/module-catalog.ts');

test('announcement draft save is group-bound and audit atomic without dispatch',()=>{
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/club\.teams t/);
  assert.match(sql,/club\.academy_groups g/);
  assert.match(sql,/INSERT INTO club\.admin_announcement_drafts/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(sql,/CHECK \(status='draft'\)/);
  assert.doesNotMatch(sql,/INSERT INTO (chat|messages|outbox|notifications)\./);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.create_admin_announcement_draft/);
  assert.match(boundary,/has_any_column_privilege\(auth_role,'club\.admin_announcement_drafts'/);
});
test('draft admin page is owner scoped, group-backed and cannot publish',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role,capability\)/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(ui,/data-testid="announcement-draft-form"/);
  assert.match(ui,/\/api\/admin\/football\/audiences/);
  assert.match(ui,/\/api\/admin\/communications\/announcement-drafts/);
  assert.match(ui,/method:'POST',credentials:'same-origin',cache:'no-store'/);
  assert.match(ui,/saved\.status!=='draft'/);
  assert.match(nav,/id: 'announcements'/);
  assert.doesNotMatch(ui,/\/api\/admin\/messages|sendPush|publishAnnouncement|@html/);
});
