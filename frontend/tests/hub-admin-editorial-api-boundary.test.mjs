import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
const endpoint = read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const migration = read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110183000_HubAdminDraftReader.cs');
const program = read('backend/src/LefteriaFc.Api/Program.cs');
const grants = read('ops/postgres/web-admin-auth-grants.sql');
const permissions = read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary = read('ops/postgres/admin-hub-editorial-boundary.sql');

test('real HUB editorial GET API requires authenticated web-admin and fresh scope', () => {
  assert.match(endpoint, /MapGet\("\/api\/admin\/hub\/editorial"/);
  assert.match(endpoint, /RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(endpoint, /admin\.CurrentAsync\(cookie, token\)/);
  assert.match(endpoint, /session\.User\.Organizations\.Any\(org => org\.Id == organization_id\)/);
  assert.match(endpoint, /AdminPermissions\.Has\(session\.User\.Role, "news:read"\)/);
  assert.match(endpoint, /context\.Request\.Query\["organization_id"\]\.Count != 1/);
  assert.match(endpoint, /SELECT hub\.read_admin_editorial\(@digest,@owner\)::text/);
  assert.match(endpoint, /private, no-store/);
  assert.match(program, /app\.MapAdminHub\(\)/);
});

test('SQL rechecks admin session and current owner grant, limits metadata only', () => {
  assert.match(migration, /SECURITY DEFINER/);
  assert.match(migration, /SET search_path=pg_catalog,pg_temp/);
  assert.match(migration, /s\.token_digest=_digest AND s\.revoked_at IS NULL/);
  assert.match(migration, /s\.expires_at>clock_timestamp\(\)/);
  assert.match(migration, /g\.admin_account_id=a\.id AND g\.organization_id=_owner/);
  assert.match(migration, /v_role NOT IN \('manager','super_admin','editor'\)/);
  assert.match(migration, /WHERE c\.organization_id=_owner/);
  assert.match(migration, /LIMIT 100/);
  assert.doesNotMatch(migration, /body_text|media_assets|fan_subscriptions|provider_reference/);
  assert.match(migration, /REVOKE ALL ON FUNCTION hub\.read_admin_editorial/);
});

test('admin role may execute exactly the scoped function, never raw Hub tables', () => {
  assert.match(grants, /GRANT USAGE ON SCHEMA identity,club,audit,public_content,pos,hub/);
  assert.match(grants, /GRANT EXECUTE ON FUNCTION hub\.read_admin_editorial\(text,text\) TO :"admin_auth_role"/);
  assert.match(grants, /REVOKE ALL ON FUNCTION hub\.read_admin_editorial\(text,text\) FROM PUBLIC, :"app_role"/);
  assert.match(grants, /\\ir admin-hub-editorial-boundary.sql/);
  assert.match(permissions, /'hub\.read_admin_editorial\(text,text\)'::regprocedure/);
  assert.match(boundary, /routine\.prosecdef/);
  assert.match(boundary, /has_any_column_privilege\(auth_role/);
  assert.match(boundary, /has_any_column_privilege\(mobile_role/);
});
