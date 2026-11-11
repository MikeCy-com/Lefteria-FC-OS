import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const base=new URL('../../',import.meta.url);
const read=p=>readFileSync(new URL(p,base),'utf8');
const page=read('frontend/src/routes/admin/groups/+page.svelte');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminGroupCreateEndpoints.cs');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108123000_AdminGroupCreate.cs');
const boundary=read('ops/postgres/admin-group-create-boundary.sql');
const grants=read('ops/postgres/web-admin-auth-grants.sql');

test('admin creates actual canonical teams/groups, with no chat or announcement delivery',()=>{
  assert.match(sql,/INSERT INTO club\.teams/);
  assert.match(sql,/INSERT INTO club\.academy_groups/);
  assert.match(sql,/PERFORM identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/pg_advisory_xact_lock/);
  assert.match(sql,/IF v_count>=150 THEN/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.create_admin_group\(text,text,text,text\) FROM PUBLIC/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.create_admin_group\(text,text,text,text\)/);
  assert.match(boundary,/has_any_column_privilege\(auth_name,c\.oid,'SELECT,INSERT,UPDATE,REFERENCES'\)/);
  assert.doesNotMatch(sql,/INSERT INTO (club\.players|club\.player_academy_groups|chat\.|messages\.)/);
});

test('group create form requires live manager capability and verifies server result before reload',()=>{
  assert.match(api,/\.RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role,capability\)/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(api,/keys\.Add\(property\.Name\)/);
  assert.match(page,/data-testid="admin-group-create-form"/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions,/);
  assert.match(page,/'teams:write' : 'academy:write'/);
  assert.match(page,/method:'POST', credentials:'same-origin', cache:'no-store'/);
  assert.match(page,/'X-Lefteria-Admin':'1'/);
  assert.match(page,/response\.status === 401 \|\| response\.status === 403/);
  assert.match(page,/response\.status === 409/);
  assert.match(page,/^\s*if \(!response\.ok\)/m);
  assert.match(page,/\^grp_\[0-9a-f\]\{32\}\$/);
  assert.match(page,/showCreate = false;\s*groupName = '';\s*createRequestId='';\s*retry\+\+;/);
  assert.doesNotMatch(page,/\{@html|innerHTML|\/api\/admin\/messages|\/api\/mobile\/notify/i);
});
