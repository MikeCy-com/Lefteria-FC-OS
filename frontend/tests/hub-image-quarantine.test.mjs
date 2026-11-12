import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112090000_HubPrivateImageQuarantine.cs');
const guards=read('backend/src/LefteriaFc.Api/WebAdmin/WebAdminRequestGuards.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const verify=read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');

test('small sanitized admin JPEG uploads stay private',()=>{
  assert.match(api,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/images\/quarantine"/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/sanitizer\.Sanitize\(raw, "image\/jpeg", token\)/);
  assert.match(api,/SELECT hub\.quarantine_admin_image\(@digest,@owner,@id,@version,@media,@alt,@jpeg\)::text/);
  assert.match(api,/CryptographicOperations\.ZeroMemory\(raw\)/);
  assert.match(api,/CryptographicOperations\.ZeroMemory\(clean\)/);
  assert.match(api,/status\.GetString\(\) != "quarantined"/);
  assert.match(guards,/hubImagePath/);
  assert.match(guards,/2 \* 1024 \* 1024 \+ 8192/);
});
test('SQL stores only quarantine JPEG behind scoped audited writer',()=>{
  assert.match(sql,/CREATE TABLE hub\.quarantined_image_bytes/);
  assert.match(sql,/ALTER TABLE hub\.quarantined_image_bytes ENABLE ROW LEVEL SECURITY/);
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin','editor'\)/);
  assert.match(sql,/v_content\.version<>_version OR v_content\.status<>'ai_draft'/);
  assert.match(sql,/INSERT INTO hub\.quarantined_image_bytes/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(sql,/moderation,generated_by_ai/);
  assert.match(sql,/'quarantined',false/);
  assert.doesNotMatch(sql,/CREATE FUNCTION hub\.read|SET status='published'|SET visual_review='passed'/);
});
test('no public or mobile privilege for image quarantiner',()=>{
 const sig='hub.quarantine_admin_image(text,text,uuid,bigint,uuid,text,bytea)';
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
 assert.ok(grants.includes("'"+sig+"'::regprocedure"));
 assert.ok(verify.includes("'"+sig+"'::regprocedure"));
 assert.ok(boundary.includes("'"+sig+"'::regprocedure"));
 assert.match(boundary,/has_function_privilege\(mobile_role,image_quarantiner\.oid,'EXECUTE'\)/);
});
