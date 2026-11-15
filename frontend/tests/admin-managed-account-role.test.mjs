import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const page=read('frontend/src/routes/admin/users/+page.svelte');
const api=read('backend/src/LefteriaFc.Api/WebAdmin/AdminUserDirectoryEndpoints.cs');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115960000_ManagedAdminRoleChange.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-managed-role-boundary.sql');
const permissions=read('ops/postgres/web-admin-auth-permissions-check.sql');
const integration=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminHttpChecks.ManagedRole.cs');

test('managed-role control is accessible only for eligible users with confirmation',()=>{
  assert.ok(compile(page,{filename:'admin/users/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(page,/data-testid="admin-account-role-select"/);
  assert.match(page,/data-testid="admin-account-role-submit"/);
  assert.match(page,/account\.role!=='super_admin'/);
  assert.match(page,/account\.invitation_pending/);
  assert.match(page,/account\.id!==\$auth\.session\.user\.id/);
  assert.match(page,/window\.confirm/);
  assert.match(page,/fetch\('\/api\/admin\/access\/users\/role'/);
  assert.match(page,/expected_role:account\.role/);
  assert.match(page,/expected_active:account\.active/);
  assert.match(page,/next_role:nextRole/);
  assert.match(page,/result\.role!==nextRole/);
  assert.match(page,/rolePending/);
  assert.doesNotMatch(page,/localStorage|{@html|innerHTML/);
});

test('API and SQL separately restrict role mutations to active Super Admins',()=>{
  assert.match(api,/MapPost\("\/api\/admin\/access\/users\/role"/);
  assert.match(api,/session\.User\.Role!="super_admin"/);
  assert.match(api,/identity\.change_managed_admin_role/);
  assert.match(api,/secrets\.TokenDigest\(cookie!\)/);
  assert.match(api,/managed_admin_role_assignments_exist/);
  assert.match(api,/managed_admin_role_conflict/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/v_actor_role IS DISTINCT FROM 'super_admin'/);
  assert.match(migration,/v_actor=_subject/);
  assert.match(migration,/v_role='super_admin'/);
  assert.match(migration,/v_role<>_expected_role/);
  assert.match(migration,/v_active IS DISTINCT FROM _expected_active/);
  assert.match(migration,/FOR UPDATE/);
  assert.match(migration,/FOR SHARE/);
  for(const table of ['football_city_admin_grants','football_location_admin_grants',
    'football_team_admin_grants','admin_staff_links','cashier_assignments'])
    assert.ok(migration.includes(table),'Missing guard for '+table);
  assert.match(migration,/UPDATE identity\.admin_sessions/);
  assert.match(migration,/admin\.account\.role_change/);
  assert.doesNotMatch(api,/UPDATE identity\.admin_accounts|DELETE FROM identity\.admin_accounts/);
});

test('role writers are non-public and blocked to mobile, with isolated integration proofs',()=>{
  assert.match(grants,/REVOKE ALL ON FUNCTION identity\.change_managed_admin_role/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION identity\.change_managed_admin_role/);
  assert.match(grants,/identity\.change_managed_admin_role\(text,uuid,text,boolean,text\)'::regprocedure/);
  assert.match(permissions,/admin-managed-role-boundary\.sql/);
  assert.match(boundary,/has_function_privilege\(mobile,routine,'EXECUTE'\)/);
  assert.ok(boundary.includes("a.grantee=0 AND a.privilege_type='EXECUTE'"));
  assert.match(boundary,/UPDATE identity\.admin_accounts SET role='coach' WHERE false/);
  assert.match(integration,/managed_admin_role_assignments_exist/);
  assert.match(integration,/managed_admin_role_conflict/);
  assert.match(integration,/CountManagedRoleAuditAsync/);
  assert.match(integration,/SeedManagedRolePosAssignmentAsync/);
  assert.match(integration,/DeactivateManagedRolePosAssignmentAsync/);
  assert.match(migration,/cashier_assignments[\\s\\S]*?AND is_active/);
  assert.match(integration,/not_authenticated/);
  assert.match(integration,/revoked/);
});
