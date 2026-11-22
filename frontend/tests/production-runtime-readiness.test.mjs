import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const base=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,base),'utf8');

const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261121991000_RuntimeSchemaReadiness.cs');
const checks=read('backend/src/LefteriaFc.Api/Health/RuntimeReadiness.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Health/HealthEndpoints.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const runtime=read('ops/postgres/identity-app-permissions-check.sql');
const integration=read('backend/tests/LefteriaFc.Identity.IntegrationTests/IdentityHttpChecks.Readiness.cs');
const admin=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminHttpChecks.Readiness.cs');

test('runtime schema compatibility is boolean, least-privilege and compatible with additive migration',()=>{
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/CREATE FUNCTION identity\.runtime_schema_ready/);
  assert.match(sql,/FROM public\."__EFMigrationsHistory"/);
  assert.match(sql,/v_present AND v_before=_expected_count/);
  assert.match(sql,/WHERE "MigrationId"<=_latest/);
  assert.match(sql,/REVOKE ALL ON FUNCTION identity\.runtime_schema_ready/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION identity\.runtime_schema_ready/);
  assert.doesNotMatch(grants,/GRANT SELECT ON public\."__EFMigrationsHistory"/);
  assert.match(runtime,/has_table_privilege\(current_user,\s*'public\."__EFMigrationsHistory"','SELECT'\)/);
  assert.match(runtime,/identity\.runtime_schema_ready\('20991231235959_NotInstalled',9999\)/);
  assert.match(integration,/GetMigrations\(\)/);
  assert.match(integration,/migrations\.Length\+1/);
});

test('health readiness checks the active app role and optional independent administrator',()=>{
  assert.match(checks,/MobileProof/);
  assert.match(checks,/current_database\(\) NOT IN/);
  assert.match(checks,/r\.rolcanlogin AND NOT \(r\.rolsuper OR r\.rolinherit/);
  assert.match(checks,/NOT EXISTS \(SELECT 1 FROM pg_auth_members/);
  assert.match(checks,/NOT has_column_privilege\(current_user,'identity\.admin_accounts','password_hash','SELECT'\)/);
  assert.match(checks,/identity\.runtime_schema_ready\(@last,@count\)/);
  assert.match(checks,/row_security_active\('identity\.mobile_identities'\)/);
  assert.match(checks,/row_security_active\('club\.calendar_responses'\)/);
  assert.match(checks,/club\.read_mobile_chat_groups/);
  assert.match(checks,/hub\.read_mobile_player_classrooms/);
  assert.match(checks,/if\(!adminOptions\.Value\.Enabled\)return true/);
  assert.match(checks,/WebAdminConnections/);
  assert.match(checks,/club\.create_admin_native_coach/);
  assert.match(checks,/identity\.create_managed_admin/);
  assert.match(checks,/NOT has_column_privilege\(current_user,'identity\.mobile_identities','id','SELECT'\)/);
  assert.doesNotMatch(checks,/MigrateAsync|GRANT EXECUTE|DROP TABLE|INSERT INTO|UPDATE identity\./);
});

test('anonymous health exposes only status, 503 on failed readiness, and no stale caching',()=>{
  assert.match(endpoint,/RuntimeReadiness\.CheckAsync/);
  assert.match(endpoint,/new HealthResponse\("unavailable"\)/);
  assert.match(endpoint,/StatusCodes\.Status503ServiceUnavailable/);
  assert.match(endpoint,/CacheControl="no-store, max-age=0"/);
  assert.match(endpoint,/new HealthResponse\("ready"\)/);
  assert.match(integration,/GetAsync\("\/api\/health\/ready"\)/);
  assert.match(admin,/GetAsync\("\/api\/health\/ready"\)/);
  assert.match(admin,/TryGetProperty\("migration"/);
  assert.match(admin,/TryGetProperty\("database"/);
});
