import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110190000_HubAdminDraftCreate.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');
const checker=read('ops/postgres/web-admin-auth-permissions-check.sql');

test('draft POST rechecks cookie session, owner and write permission',()=>{
  assert.match(api,/MapPost\("\/api\/admin\/hub\/editorial"/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/admin\.CurrentAsync\(cookie, token\)/);
  assert.match(api,/session\.User\.Organizations\.Any/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role, "news:write"\)/);
  assert.match(api,/context\.Request\.Query\["organization_id"\]\.Count != 1/);
  assert.match(api,/SELECT hub\.create_admin_draft\(@digest,@owner,@id,@revision_id/);
  assert.match(api,/Results\.Content\(raw, "application\/json", statusCode: 201\)/);
});

test('draft writer is server-only, transactionally audited and never publishes',()=>{
  assert.match(sql,/CREATE FUNCTION hub\.create_admin_draft/);
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/s\.token_digest=_digest AND s\.revoked_at IS NULL/);
  assert.match(sql,/g\.admin_account_id=a\.id AND g\.organization_id=_owner/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin','editor'\)/);
  assert.match(sql,/INSERT INTO hub\.content/);
  assert.match(sql,/INSERT INTO hub\.revisions/);
  assert.match(sql,/INSERT INTO hub\.review_events/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(sql,/'ai_draft'/);
  assert.match(sql,/'pending','pending'/);
  assert.doesNotMatch(sql,/UPDATE hub\.content|status','published'|provider_reference|Payabl/i);
});

test('no raw table grant and only named function callable by web admin',()=>{
  const signature='hub.create_admin_draft(text,text,uuid,uuid,text,text,text,boolean,boolean,smallint,smallint)';
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+signature));
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+signature));
  assert.ok(checker.includes("'"+signature+"'::regprocedure"));
  assert.ok(boundary.includes("'"+signature+"'::regprocedure"));
  assert.match(boundary,/has_any_column_privilege\(mobile_role/);
  assert.match(boundary,/has_any_column_privilege\(auth_role/);
});
