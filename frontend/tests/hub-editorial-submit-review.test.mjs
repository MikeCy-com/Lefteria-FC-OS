import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const source=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const sql=source('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110193000_HubEditorialSubmitReview.cs');
const api=source('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=source('ops/postgres/web-admin-auth-grants.sql');
const boundary=source('ops/postgres/admin-hub-editorial-boundary.sql');
const privilegeCheck=source('ops/postgres/web-admin-auth-permissions-check.sql');

test('HUB submit-review requires a cookie, exact organization and write permission',()=>{
  assert.match(api,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/submit-review"/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/admin\.CurrentAsync\(cookie, token\)/);
  assert.match(api,/session\.User\.Organizations\.Any\(org => org.Id == organization_id\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role, "news:write"\)/);
  assert.match(api,/SELECT hub\.submit_admin_draft_review\(@digest,@owner,@id,@version\)::text/);
  assert.match(api,/hub_review_conflict/);
  assert.match(api,/hub_draft_not_found/);
  assert.match(api,/statusCode: 201/); // existing creation path remains distinct
});

test('SQL changes only unpublished draft -> review-required, locks scope and audits atomically',()=>{
  assert.match(sql,/CREATE FUNCTION hub\.submit_admin_draft_review/);
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/s\.token_digest=_digest AND s\.revoked_at IS NULL/);
  assert.match(sql,/s\.expires_at>clock_timestamp\(\)/);
  assert.match(sql,/g\.organization_id=_owner/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin','editor'\)/);
  assert.match(sql,/WHERE id=_content AND organization_id=_owner FOR UPDATE/);
  assert.match(sql,/v_content\.version<>_version OR v_content\.status<>'ai_draft'/);
  assert.match(sql,/SET status='review_required',version=version\+1/);
  assert.match(sql,/INSERT INTO hub\.review_events/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.doesNotMatch(sql,/SET status='approved'|SET status='published'|provider_reference|payabl|INSERT INTO hub\.media_assets/i);
});

test('Dedicated admin SQL function cannot be called by mobile or PUBLIC roles',()=>{
  const signature='hub.submit_admin_draft_review(text,text,uuid,bigint)';
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+signature+' TO :"admin_auth_role"'));
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+signature+' FROM PUBLIC, :"app_role"'));
  assert.ok(privilegeCheck.includes("'"+signature+"'::regprocedure"));
  assert.ok(boundary.includes("'"+signature+"'::regprocedure"));
  assert.match(boundary,/has_function_privilege\(mobile_role,reviewer\.oid,'EXECUTE'\)/);
  assert.match(boundary,/has_any_column_privilege\(mobile_role/);
});
