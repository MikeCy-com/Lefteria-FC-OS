import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112100000_HubPrivateImagePreview.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const verify=read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');

test('preview endpoint serves only verified JPEG to management',()=>{
  assert.match(api,/MapGet\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/images\/\{mediaId:guid\}\/preview"/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/SELECT hub\.read_admin_quarantined_image\(@digest,@owner,@id,@media\)::text/);
  assert.match(api,/SHA256\.HashData\(jpeg\)/);
  assert.match(api,/Convert\.FromBase64String\(raw\)/);
  assert.match(api,/Results\.File\(jpeg, "image\/jpeg"\)/);
  assert.match(api,/Content-Security-Policy/);
  assert.match(api,/X-Content-Type-Options/);
  assert.match(api,/private, no-store/);
});
test('database preview locks auth and owner, checks media integrity without granting raw reads',()=>{
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/c\.organization_id=_owner/);
  assert.match(sql,/m\.moderation='quarantined'/);
  assert.match(sql,/m\.generated_by_ai=false/);
  assert.match(sql,/sha256\(v_bytes\)/);
  assert.match(sql,/FOR SHARE OF c,r,m,q/);
  assert.doesNotMatch(sql,/GRANT SELECT ON|SET visual_review='passed'|SET status='published'/);
});
test('preview access is callable only by separate web-admin auth role',()=>{
 const sig='hub.read_admin_quarantined_image(text,text,uuid,uuid)';
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
 assert.ok(verify.includes("'"+sig+"'::regprocedure"));
 assert.ok(boundary.includes("'"+sig+"'::regprocedure"));
 assert.match(boundary,/has_function_privilege\(mobile_role,image_previewer\.oid,'EXECUTE'\)/);
});
