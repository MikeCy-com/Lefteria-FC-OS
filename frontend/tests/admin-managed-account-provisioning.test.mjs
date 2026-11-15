import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const page=read('frontend/src/routes/admin/users/+page.svelte');
const endpoint=read('backend/src/LefteriaFc.Api/WebAdmin/AdminUserDirectoryEndpoints.cs');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115250000_AdminManagedAccounts.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const policies=read('ops/postgres/web-admin-auth-permissions-check.sql');

test('super admin role-limited credential creation is a real POST and never returns passwords',()=>{
  assert.ok(compile(page,{filename:'admin/users/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(page,/data-testid="admin-managed-user-creation"/);
  assert.match(page,/data-testid="admin-create-account-form"/);
  assert.match(page,/data-testid="admin-managed-user-submit"/);
  assert.match(page,/method:'POST'/);
  assert.match(page,/fetch\('\/api\/admin\/access\/users'/);
  assert.match(page,/'X-Lefteria-Admin':'1'/);
  assert.match(page,/password='';confirmPassword=''/);
  assert.match(page,/response\.status!==201/);
  assert.match(page,/selectedOwners\.every/);
  assert.doesNotMatch(page,/super_admin','Super Admin'|<option value="super_admin"/);
  assert.match(endpoint,/MapPost\("\/api\/admin\/access\/users"/);
  assert.match(endpoint,/session\.User\.Role!="super_admin"/);
  assert.match(endpoint,/BCrypt\.Net\.BCrypt\.HashPassword\(password,workFactor:12\)/);
  assert.match(endpoint,/statusCode:201/);
  assert.doesNotMatch(endpoint,/return Results\.Json\(new \{[^}]*password/i);
});

test('account creation is independently authorization-checked inside narrow PostgreSQL writer',()=>{
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/identity\.lock_admin_club_authority\(_digest,v_owner\)/);
  assert.match(migration,/a\.role='super_admin'/);
  assert.match(migration,/NOT IN \('lefteria_fc','lefteria_fc_academy'\)/);
  assert.match(migration,/INSERT INTO identity\.admin_accounts/);
  assert.match(migration,/INSERT INTO identity\.admin_organization_grants/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.match(migration,/admin\.account\.create/);
  assert.doesNotMatch(migration,/role\s+IN\s+\([^)]*'super_admin',/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION identity\.create_managed_admin/);
  assert.match(grants,/REVOKE ALL ON FUNCTION identity\.create_managed_admin/);
  assert.match(policies,/has_any_column_privilege\(current_user,'identity\.admin_accounts','INSERT,UPDATE,REFERENCES'\)/);
  assert.match(policies,/has_any_column_privilege\(current_user,'identity\.admin_organization_grants','INSERT,UPDATE,REFERENCES'\)/);
});

test('input and response are bounded, origin-guarded and reject unauthorized scope or privilege',()=>{
  assert.match(endpoint,/owners\.Distinct\(StringComparer\.Ordinal\)\.Count\(\)!=owners\.Length/);
  assert.match(endpoint,/!granted\.Contains\(owner\)/);
  assert.match(endpoint,/bytes<12 \|\| bytes>72/);
  assert.match(endpoint,/permittedRoles\.Contains\(request\.Role\)/);
  assert.match(endpoint,/PasswordHashQueue\.WaitAsync\(0,token\)/);
  assert.match(endpoint,/NpgsqlDbType\.Array\|NpgsqlDbType\.Text/);
  assert.match(endpoint,/secrets\.TokenDigest\(cookie!\)/);
  assert.match(endpoint,/ExcludeFromDescription\(\)/);
  const guard=read('backend/src/LefteriaFc.Api/WebAdmin/WebAdminRequestGuards.cs');
  assert.match(guard,/X-Lefteria-Admin/);
  assert.match(guard,/TrustedMutation/);
});
