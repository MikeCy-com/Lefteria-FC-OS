import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112110000_HubManagerVisualRejection.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const verify=read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');

test('visual rejection requires management authority and current version',()=>{
 assert.match(api,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/images\/\{mediaId:guid\}\/reject"/);
 assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
 assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
 assert.match(api,/SELECT hub\.reject_admin_hub_image\(@digest,@owner,@id,@version,@media\)::text/);
 assert.match(api,/body\.Version == long\.MaxValue/);
 assert.match(api,/private, no-store/);
});
test('visual rejection remains unpublished, audited, and refuses repeated decisions',()=>{
 assert.match(sql,/SECURITY DEFINER/);
 assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
 assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
 assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
 assert.match(sql,/v_content\.version<>_version OR v_content\.status<>'review_required'/);
 assert.match(sql,/v_revision\.visual_review<>'pending'/);
 assert.match(sql,/m[.]id=_media AND revision_id=v_revision[.]id|id=_media AND revision_id=v_revision[.]id/);
 assert.match(sql,/SET moderation='rejected'/);
 assert.match(sql,/SET visual_review='rejected'/);
 assert.match(sql,/INSERT INTO hub\.review_events/);
 assert.match(sql,/INSERT INTO audit\.security_events/);
 assert.doesNotMatch(sql,/SET status='published'|SET status='approved'|SET visual_review='passed'|GRANT SELECT ON/);
});
test('no mobile or public review rights',()=>{
 const sig='hub.reject_admin_hub_image(text,text,uuid,bigint,uuid)';
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
 assert.ok(verify.includes("'"+sig+"'::regprocedure"));
 assert.ok(boundary.includes("'"+sig+"'::regprocedure"));
 assert.match(boundary,/has_function_privilege\(mobile_role,visual_rejecter\.oid,'EXECUTE'\)/);
});
