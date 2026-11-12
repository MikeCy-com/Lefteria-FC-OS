import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112190000_HubManagerFinalApproval.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');
const verify=read('ops/postgres/web-admin-auth-permissions-check.sql');
test('only current manager can final-approve a reviewed draft',()=>{
 assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
 assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
 assert.match(sql,/v_content\.version<>_version OR v_content\.status<>'review_required'/);
 assert.match(sql,/v_revision\.text_review<>'passed' OR v_revision\.visual_review<>'passed'/);
 assert.match(sql,/SET status='approved',approved_by=v_actor/);
 assert.match(sql,/INSERT INTO hub\.review_events/);
 assert.match(sql,/INSERT INTO audit\.security_events/);
 assert.doesNotMatch(sql,/SET status='published'|published_at=clock_timestamp\(\)/);
 assert.match(api,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/final-approve"/);
 assert.match(api,/SELECT hub\.approve_admin_hub_content\(@digest,@owner,@id,@version\)::text/);
 assert.match(api,/ApproveAdminHubContent/);
});
test('approval fails closed for changed or invalid images',()=>{
 assert.match(sql,/LEFT JOIN hub\.quarantined_image_bytes b/);
 assert.match(sql,/m\.moderation<>'reviewed'/);
 assert.match(sql,/m\.sha256<>encode\(sha256\(b\.jpeg\),'hex'\)/);
 assert.match(sql,/m\.byte_count<>octet_length\(b\.jpeg\)/);
});
test('final approval remains inaccessible to mobile and AI worker',()=>{
 const sig='hub.approve_admin_hub_content(text,text,uuid,bigint)';
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
 assert.ok(boundary.includes("'"+sig+"'::regprocedure"));
 assert.match(boundary,/has_function_privilege\(mobile_role,content_approver\.oid,'EXECUTE'\)/);
 assert.ok(verify.includes("'"+sig+"'::regprocedure"));
});
