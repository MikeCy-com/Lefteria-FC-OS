import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL('../../' + path, import.meta.url), 'utf8');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110200000_HubAdminDraftDetail.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const verify=read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');

test('draft detail API requires current admin session, correct org and read capability',()=>{
  assert.match(api,/MapGet\("\/api\/admin\/hub\/editorial\/\{id:guid\}"/);
  assert.match(api,/\.RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/admin\.CurrentAsync\(cookie, token\)/);
  assert.match(api,/session\.User\.Organizations\.Any\(org => org.Id == organization_id\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role, "news:read"\)/);
  assert.match(api,/SELECT hub\.read_admin_editorial_detail\(@digest,@owner,@id\)::text/);
  assert.match(api,/private, no-store/);
  assert.match(api,/hub_draft_not_found/);
});

test('SQL only reads scoped revisions and redacted descriptors, not storage or subscriptions',()=>{
  assert.match(sql,/CREATE FUNCTION hub\.read_admin_editorial_detail/);
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/s\.token_digest=_digest AND s\.revoked_at IS NULL/);
  assert.match(sql,/s\.expires_at>clock_timestamp\(\)/);
  assert.match(sql,/g\.admin_account_id=a\.id AND g\.organization_id=_owner/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin','editor'\)/);
  assert.match(sql,/WHERE c\.id=_id AND c\.organization_id=_owner/);
  assert.match(sql,/r\.body_text/);
  assert.match(sql,/m\.moderation/);
  assert.match(sql,/m\.generated_by_ai/);
  assert.doesNotMatch(sql,/m\.storage_key|m\.sha256|fan_subscriptions|provider_reference|SELECT \*/);
  assert.doesNotMatch(sql,/INSERT INTO hub\.|UPDATE hub\.|DELETE FROM hub\./);
});

test('privileged detail SQL is not callable from mobile and has no raw table access',()=>{
  const signature='hub.read_admin_editorial_detail(text,text,uuid)';
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+signature+' TO :"admin_auth_role"'));
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+signature+' FROM PUBLIC, :"app_role"'));
  assert.ok(verify.includes("'"+signature+"'::regprocedure"));
  assert.ok(boundary.includes("'"+signature+"'::regprocedure"));
  assert.match(boundary,/has_function_privilege\(mobile_role,detail_reader\.oid,'EXECUTE'\)/);
  assert.match(boundary,/has_any_column_privilege\(auth_role/);
});
