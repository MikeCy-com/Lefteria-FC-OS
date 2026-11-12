import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = p => readFileSync(new URL('../../'+p, import.meta.url), 'utf8');
const sql = read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112080000_HubReturnRejectedDraft.cs');
const api = read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants = read('ops/postgres/web-admin-auth-grants.sql');
const verify = read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary = read('ops/postgres/admin-hub-editorial-boundary.sql');

test('return for revision is exclusively manager-authorized and version checked', () => {
  assert.match(api, /MapPost\("\/api\/admin\/hub\/editorial\/\{id:guid\}\/return-for-revision"/);
  assert.match(api, /SELECT hub\.return_rejected_admin_draft\(@digest,@owner,@id,@version\)::text/);
  assert.match(api, /session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api, /RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api, /body\.Version == long\.MaxValue/);
  assert.match(api, /private, no-store/);
});

test('SQL requires text rejection and never erases original review or publishes', () => {
  assert.match(sql, /SECURITY DEFINER/);
  assert.match(sql, /SET search_path=pg_catalog,pg_temp/);
  assert.match(sql, /identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql, /v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql, /v_content\.version<>_version OR v_content\.status<>'review_required'/);
  assert.match(sql, /v_revision\.text_review<>'rejected'/);
  assert.match(sql, /SET status='ai_draft',version=version\+1/);
  assert.match(sql, /INSERT INTO hub\.review_events/);
  assert.match(sql, /INSERT INTO audit\.security_events/);
  assert.doesNotMatch(sql, /UPDATE hub\.revisions SET|DELETE FROM hub\.revisions|SET status='approved'|SET status='published'/);
});

test('manager return function is never granted to mobile or publicly executable', () => {
  const sig = 'hub.return_rejected_admin_draft(text,text,uuid,bigint)';
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
  assert.ok(grants.includes("'"+sig+"'::regprocedure"));
  assert.ok(verify.includes("'"+sig+"'::regprocedure"));
  assert.ok(boundary.includes("'"+sig+"'::regprocedure"));
  assert.match(boundary, /has_function_privilege\(mobile_role,rejection_returner\.oid,'EXECUTE'\)/);
});
