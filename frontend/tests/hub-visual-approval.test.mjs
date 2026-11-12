import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112180000_HubManagerVisualApproval.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');
const permissionCheck=read('ops/postgres/web-admin-auth-permissions-check.sql');

test('visual signoff is manager-only and version-bound, never a publish action',()=>{
 assert.match(api,/MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/visual-approve"/);
 assert.match(api,/SELECT hub\.approve_admin_hub_visual\(@digest,@owner,@id,@version\)::text/);
 assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
 assert.match(api,/ApproveAdminHubVisual/);
 assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
 assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
 assert.match(sql,/v_content\.version<>_version OR v_content\.status<>'review_required'/);
 assert.match(sql,/v_revision\.visual_review<>'pending'/);
 assert.match(sql,/UPDATE hub\.revisions SET visual_review='passed'/);
 assert.match(sql,/INSERT INTO hub\.review_events/);
 assert.match(sql,/INSERT INTO audit\.security_events/);
 assert.doesNotMatch(sql,/SET status='approved'|SET status='published'|INSERT INTO club\.app_private_messages/);
});
test('approval verifies image bytes, rejects unauthorized artifacts, and leaves text review separate',()=>{
 assert.match(sql,/LEFT JOIN hub\.quarantined_image_bytes b ON b\.media_id=m\.id/);
 assert.match(sql,/m\.byte_count<>octet_length\(b\.jpeg\)/);
 assert.match(sql,/m\.sha256<>encode\(sha256\(b\.jpeg\),'hex'\)/);
 assert.match(sql,/m\.moderation<>'quarantined'/);
 assert.match(sql,/m\.generated_by_ai/);
 assert.match(sql,/UPDATE hub\.media_assets SET moderation='reviewed'/);
 assert.doesNotMatch(sql,/SET text_review='passed'|UPDATE hub\.content\s+SET status/);
});
test('only admin role may invoke visual approval and it is structurally guarded',()=>{
 const sig='hub.approve_admin_hub_visual(text,text,uuid,bigint)';
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
 assert.ok(permissionCheck.includes("'"+sig+"'::regprocedure"));
 assert.ok(boundary.includes("'"+sig+"'::regprocedure"));
 assert.match(boundary,/has_function_privilege\(mobile_role,visual_approver\.oid,'EXECUTE'\)/);
 assert.match(boundary,/visual_approver\.prosecdef/);
});
