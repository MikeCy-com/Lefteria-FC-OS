import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110203000_HubAdminTextReview.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');
const verify=read('ops/postgres/web-admin-auth-permissions-check.sql');

test('text review decision is management-only with live owner verification',()=>{
  assert.match(api,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/text-review"/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/session\.User\.Organizations\.Any\(org => org.Id == organization_id\)/);
  assert.match(api,/SELECT hub\.review_admin_text\(@digest,@owner,@id,@version,@decision\)::text/);
  assert.match(api,/body\.Decision is not \("passed" or "rejected"\)/);
  assert.match(api,/private, no-store/);
});

test('SQL reviews latest text revision and never reviews images or publishes content',()=>{
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/s\.expires_at>clock_timestamp\(\)/);
  assert.match(sql,/g\.organization_id=_owner/);
  assert.match(sql,/WHERE id=_content AND organization_id=_owner/);
  assert.match(sql,/v_content\.version<>_version OR v_content\.status<>'review_required'/);
  assert.match(sql,/ORDER BY revision DESC LIMIT 1/);
  assert.match(sql,/v_revision\.text_review<>'pending'/);
  assert.match(sql,/UPDATE hub\.revisions SET text_review=_decision/);
  assert.match(sql,/INSERT INTO hub\.review_events/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(sql,/'visual_review',v_revision\.visual_review/);
  assert.doesNotMatch(sql,/UPDATE hub\.media_assets|SET visual_review=|SET status='approved'|SET status='published'|provider_reference/);
});

test('text reviewer is privileged only for admin-auth SQL role',()=>{
  const fn='hub.review_admin_text(text,text,uuid,bigint,text)';
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+fn+' FROM PUBLIC, :"app_role"'));
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+fn+' TO :"admin_auth_role"'));
  assert.ok(verify.includes("'"+fn+"'::regprocedure"));
  assert.ok(boundary.includes("'"+fn+"'::regprocedure"));
  assert.match(boundary,/has_function_privilege\(mobile_role,text_reviewer\.oid,'EXECUTE'\)/);
  assert.match(boundary,/has_any_column_privilege\(mobile_role/);
});
