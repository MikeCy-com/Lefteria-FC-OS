import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const page=read('frontend/src/routes/admin/users/+page.svelte');
const route=read('backend/src/LefteriaFc.Api/WebAdmin/AdminUserDirectoryEndpoints.cs');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115910000_ManagedAdminStatus.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-managed-status-boundary.sql');
const checks=read('ops/postgres/web-admin-auth-permissions-check.sql');
const accountAuth=read('backend/src/LefteriaFc.Infrastructure/WebAdmin/PostgresWebAdminRepository.cs');
const integration=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminHttpChecks.ManagedStatus.cs');

test('Super Admin-only account controls verify request result before refreshing the list',()=>{
  assert.ok(compile(page,{filename:'admin/users/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(page,/data-testid="admin-account-status-toggle"/);
  assert.match(page,/window\.confirm/);
  assert.match(page,/fetch\('\/api\/admin\/access\/users\/status'/);
  assert.match(page,/expectedRole:account\.role/);
  assert.match(page,/expectedActive:account\.active/);
  assert.match(page,/statusPending/);
  assert.match(page,/account\.role!=='super_admin'/);
  assert.match(page,/account\.id!==\$auth\.session\.user\.id/);
  assert.match(page,/result\.active!==next/);
  assert.doesNotMatch(page,/localStorage|{@html|innerHTML/);
});

test('Status mutation is independently gated in API and locked inside PostgreSQL',()=>{
  assert.match(route,/MapPost\("\/api\/admin\/access\/users\/status"/);
  assert.match(route,/session\.User\.Role!="super_admin"/);
  assert.match(route,/request\.Id==session\.User\.Id/);
  assert.match(route,/identity\.set_managed_admin_status/);
  assert.match(route,/managed_admin_status_conflict/);
  assert.match(route,/secrets\.TokenDigest\(cookie!\)/);
  assert.match(route,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/v_actor_role IS DISTINCT FROM 'super_admin'/);
  assert.match(migration,/v_actor=_subject/);
  assert.match(migration,/v_subject_role='super_admin'/);
  assert.match(migration,/FOR UPDATE/);
  assert.match(migration,/actor_grant\.organization_id=v_owner FOR SHARE/);
  assert.match(migration,/v_owner_count=0/);
  assert.match(migration,/v_subject_active IS DISTINCT FROM _expected_active/);
  assert.match(migration,/UPDATE identity\.admin_sessions/);
  assert.match(migration,/admin\.account\.suspend/);
  assert.match(migration,/admin\.account\.reactivate/);
  assert.match(accountAuth,/AND a\.role=@role\s+FOR SHARE OF a\s+RETURNING id/);
  assert.doesNotMatch(route,/UPDATE identity\.admin_accounts|DELETE FROM identity\.admin_accounts/);
});

test('Only the admin auth role can invoke the explicitly reviewed status writer',()=>{
  assert.match(grants,/REVOKE ALL ON FUNCTION identity\.set_managed_admin_status/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION identity\.set_managed_admin_status/);
  assert.match(grants,/TO :"admin_auth_role"/);
  assert.match(grants,/FROM PUBLIC, :"app_role"/);
  assert.match(grants,/identity\.set_managed_admin_status\(text,uuid,text,boolean,boolean\)'::regprocedure/);
  assert.match(checks,/admin-managed-status-boundary\.sql/);
  assert.match(boundary,/has_function_privilege\(mobile_name,routine,'EXECUTE'\)/);
  assert.match(boundary,/has_function_privilege\('public',routine,'EXECUTE'\)/);
  assert.match(boundary,/UPDATE identity\.admin_accounts SET is_active=false WHERE false/);
  assert.match(integration,/managed_admin_status_denied/);
  assert.match(integration,/managed_admin_status_conflict/);
  assert.match(integration,/credentials_invalid/);
  assert.match(integration,/revoked_at IS NULL/);
});
