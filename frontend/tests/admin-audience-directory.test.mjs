import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const page=read('frontend/src/routes/admin/groups/+page.svelte');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminAudienceEndpoints.cs');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108120000_AdminAudienceDirectory.cs');
const grant=read('ops/postgres/web-admin-auth-grants.sql');
const border=read('ops/postgres/admin-audience-directory-boundary.sql');

test('canonical audiences use two roster owners and deduplicated live memberships',()=>{
  assert.match(sql,/club\.roster_import_state/);
  assert.match(sql,/FROM club\.teams/);
  assert.match(sql,/FROM club\.academy_groups/);
  assert.match(sql,/FROM club\.player_academy_groups/);
  assert.match(sql,/UNION\s+SELECT/);
  for(const state of ['p.is_active','NOT p.is_archived','NOT p.is_transferred'])
    assert.ok(sql.includes(state),state);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.read_admin_audiences\(text,text\) FROM PUBLIC/);
  assert.match(grant,/GRANT EXECUTE ON FUNCTION club\.read_admin_audiences\(text,text\)/);
  assert.match(border,/NOT has_function_privilege\(auth_name,f\.oid,'EXECUTE'\)/);
  assert.match(border,/has_any_column_privilege\(auth_name,c\.oid,'SELECT,INSERT,UPDATE,REFERENCES'\)/);
  assert.doesNotMatch(api,/MapPost\(|MapPut\(|MapDelete\(|notify|sendSms|push/i);
});

test('admin roster UI follows actual scoped server counts, not static or child-private records',()=>{
  assert.match(page,/data-testid="admin-live-audiences"/);
  assert.match(page,/hasAdminPermission\(\$auth\.session!\.user\.permissions, 'teams:read'\)/);
  assert.match(page,/hasAdminPermission\(\$auth\.session!\.user\.permissions, 'academy:read'\)/);
  assert.match(page,/fetch\('\/api\/admin\/football\/audiences\?'/);
  assert.match(page,/credentials:'same-origin', cache:'no-store'/);
  assert.match(page,/response\.status === 401/);
  assert.match(page,/response\.status === 403/);
  assert.match(page,/controller\.abort\(\)/);
  assert.match(page,/verifiedDirectory\(raw,owner\)/);
  assert.match(page,/group\.active_players/);
  assert.match(page,/directory\.unassigned_players/);
  assert.match(page,/Τα ταμεία|Η αποστολή μηνυμάτων δεν έχει ενεργοποιηθεί/);
  assert.doesNotMatch(page,/\{@html|innerHTML|player\.name|phone|\/api\/admin\/messages\/.*(POST|send)/i);
});
