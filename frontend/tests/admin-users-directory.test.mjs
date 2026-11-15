import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/WebAdmin/AdminUserDirectoryEndpoints.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const ui=read('frontend/src/routes/admin/users/+page.svelte');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
const catalog=read('frontend/src/lib/admin/module-catalog.ts');

test('read-only admin directory compiles and only shows role, status and organization grants',()=>{
  assert.ok(compile(ui,{filename:'admin-users/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(ui,/data-testid="admin-users-directory"/);
  assert.match(ui,/data-testid="admin-users-list"/);
  assert.match(ui,/\/api\/admin\/access\/users\?/);
  assert.match(ui,/credentials:'same-origin',cache:'no-store'/);
  assert.match(ui,/controller\.abort\(\)/);
  assert.match(ui,/\$auth\.session\?\.user\.role==='super_admin'/);
  assert.match(ui,/isValid\(raw,wantedOffset\)/);
  assert.doesNotMatch(ui,/type="password"|phone|password_hash|method:'POST'|method:'PUT'|localStorage|{@html|innerHTML/);
});

test('super admin server guard executes before any account SQL',()=>{
  assert.match(program,/app\.MapAdminUserDirectory\(\)/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/service\.CurrentAsync\(context\.Request\.Cookies\[WebAdminServices\.CookieName\], token\)/);
  const guard=api.indexOf('session.User.Role!="super_admin"');
  const db=api.indexOf('connections.OpenAsync(token)');
  assert.ok(guard>0&&db>guard);
  assert.match(api,/statusCode:403/);
  assert.match(api,/offset is <0 or >20000/);
  assert.match(api,/limit is <1 or >50/);
  assert.match(api,/Headers\.CacheControl = "private, no-store"/);
  assert.match(api,/FROM identity\.admin_accounts a/);
  assert.match(api,/identity\.admin_organization_grants g/);
  assert.match(grants,/GRANT SELECT \(id,username,display_name,role,is_active,invite_pending,otp2fa_enabled,password_hash\)/);
  assert.doesNotMatch(api,/password_hash|normalized_phone|otp2fa_enabled|username|UPDATE identity|DELETE FROM|INSERT INTO/);
});
test('staff and account directory navigation are independent role-first sections',()=>{
  assert.match(catalog,/id === 'users'.*super_admin/);
  assert.match(frame,/href="\/admin\/users"/);
  assert.match(frame,/data-testid="admin-users-link"/);
  assert.doesNotMatch(frame,/switchOrganization|admin-header-organization/);
});
