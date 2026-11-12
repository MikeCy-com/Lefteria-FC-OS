import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = path => readFileSync(new URL('../../'+path, import.meta.url), 'utf8');
const sql = read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112070000_HubEditorialDraftRevision.cs');
const api = read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants = read('ops/postgres/web-admin-auth-grants.sql');
const checks = read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary = read('ops/postgres/admin-hub-editorial-boundary.sql');

test('human revision is authenticated, scoped, and optimistic', () => {
  assert.match(api, /MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/revise"/);
  assert.match(api, /SELECT hub\.revise_admin_draft\(@digest,@owner,@id,@version,@title,@body\)::text/);
  assert.match(api, /RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api, /AdminPermissions\.Has\(session\.User\.Role, "news:write"\)/);
  assert.match(api, /body\.Version == long\.MaxValue/);
  assert.match(api, /hub_revision_conflict/);
  assert.match(api, /private, no-store/);
});

test('draft edit appends pending revision and cannot alter reviewed or published content', () => {
  assert.match(sql, /SECURITY DEFINER/);
  assert.match(sql, /SET search_path=pg_catalog,pg_temp/);
  assert.match(sql, /identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql, /v_role NOT IN \('manager','super_admin','editor'\)/);
  assert.match(sql, /WHERE id=_content AND organization_id=_owner FOR UPDATE/);
  assert.match(sql, /v_content\.version<>_version OR v_content\.status<>'ai_draft'/);
  assert.match(sql, /INSERT INTO hub\.revisions/);
  assert.match(sql, /'pending','pending'/);
  assert.match(sql, /INSERT INTO hub\.review_events/);
  assert.match(sql, /INSERT INTO audit\.security_events/);
  assert.doesNotMatch(sql, /SET status='published'|SET status='approved'|UPDATE hub\.media_assets/);
});

test('reviser has only exact admin-auth EXECUTE grant, no mobile/raw access', () => {
  const fn = 'hub.revise_admin_draft(text,text,uuid,bigint,text,text)';
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+fn+' FROM PUBLIC, :"app_role"'));
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+fn+' TO :"admin_auth_role"'));
  assert.ok(grants.includes("'"+fn+"'::regprocedure"));
  assert.ok(checks.includes("'"+fn+"'::regprocedure"));
  assert.ok(boundary.includes("'"+fn+"'::regprocedure"));
  assert.match(boundary, /has_function_privilege\(mobile_role,draft_reviser\.oid,'EXECUTE'\)/);
});
