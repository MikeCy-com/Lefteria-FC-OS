import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const base=new URL('../../',import.meta.url);
const read=p=>readFileSync(new URL(p,base),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261121153000_AdminNativeCoachRegistration.cs');
const api=read('backend/src/LefteriaFc.Api/WebAdmin/AdminNativeCoachEndpoints.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const check=read('ops/postgres/admin-native-coach-boundary.sql');
const perms=read('ops/postgres/web-admin-auth-permissions-check.sql');
const edge=read('deploy/preview/generate-edge.mjs');
const edgeTest=read('deploy/preview/reviewed-admin-operations.test.mjs');
const ui=read('frontend/src/lib/admin/AdminNativeCoachForm.svelte');
const page=read('frontend/src/routes/admin/staff/+page.svelte');
const fixture=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminHttpChecks.NativeCoach.cs');

test('native staff creation is a reviewed, Super Admin-only audited transaction',()=>{
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role IS DISTINCT FROM 'super_admin'/);
  assert.match(sql,/native_coach_super_admin_required/);
  assert.match(sql,/identity\.admin_staff_links/);
  assert.match(sql,/v_subject_role IS DISTINCT FROM 'coach'/);
  assert.match(sql,/native_coach_account_denied/);
  assert.match(sql,/club\.academy_group_location_assignments/);
  assert.match(sql,/club\.football_locations/);
  assert.match(sql,/club\.football_operational_scopes/);
  assert.match(sql,/club\.staff_teams/);
  assert.match(sql,/club\.staff_academy_groups/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(sql,/admin\.coach\.provision/);
  assert.doesNotMatch(sql,/INSERT INTO identity\.admin_accounts|INSERT INTO identity\.admin_organization_grants/);
});

test('idempotent native provenance does not pretend imported singular/plural source data',()=>{
  assert.match(sql,/CREATE TABLE club\.admin_native_coach_requests/);
  assert.match(sql,/request_id uuid PRIMARY KEY/);
  assert.match(sql,/pg_advisory_xact_lock\(hashtextextended/);
  assert.match(sql,/native_coach_request_conflict/);
  assert.match(sql,/'replayed',true/);
  assert.match(sql,/'replayed',false/);
  assert.match(sql,/from_admin boolean NOT NULL DEFAULT false/);
  assert.match(sql,/from_plural OR from_singular OR from_admin/);
  assert.match(sql,/from_plural OR from_singular OR from_group_coach OR from_admin/);
  assert.match(sql,/false,false,true/);
  assert.match(sql,/false,false,false,true/);
  assert.match(sql,/DROP FUNCTION club\.create_admin_native_coach/);
});

test('API, route and permissions independently deny forged mobile or arbitrary DB writes',()=>{
  assert.match(api,/MapPost\("\/api\/admin\/staff\/coaches"/);
  assert.match(api,/session\.User\.Role!="super_admin"/);
  assert.match(api,/session\.User\.Organizations\.Any/);
  assert.match(api,/secrets\.TokenDigest\(cookie!\)/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/ExcludeFromDescription\(\)/);
  assert.doesNotMatch(api,/INSERT INTO identity\.admin_accounts/); // not credential provisioning
  assert.match(program,/app\.MapAdminNativeCoach\(\)/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.create_admin_native_coach/);
  assert.match(perms,/admin-native-coach-boundary\.sql/);
  assert.match(perms,/club\.create_admin_native_coach\(text,text,uuid,text,text,text,uuid\)'::regprocedure/);
  assert.match(check,/has_function_privilege\(mobile_name,f\.oid,'EXECUTE'\)/);
  assert.match(check,/has_any_column_privilege\(auth_name,c\.oid,'SELECT,INSERT,UPDATE,REFERENCES'\)/);
  assert.match(edge,/\['post','\/api\/admin\/staff\/coaches'\]/);
  assert.match(edgeTest,/\['POST','\/api\/admin\/staff\/coaches'\]/);
});

test('Greek admin workflow chooses exact location, optional existing Coach identity and verified confirmation',()=>{
  assert.match(page,/AdminNativeCoachForm/);
  assert.match(page,/onSaved=\{\(\)=>coachRevision\+\+\}/);
  assert.match(page,/\$auth\.session\.user\.role==='super_admin'/);
  assert.match(ui,/admin-native-coach/);
  assert.match(ui,/\/api\/admin\/football\/territories\?organization_id=/);
  assert.match(ui,/\/api\/admin\/access\/users\?offset=/);
  assert.match(ui,/role==='coach'/);
  assert.match(ui,/site\.city_id|s\.city_id===g\.city_id/);
  assert.match(ui,/window\.confirm/);
  assert.match(ui,/window\.crypto\.randomUUID\(\)/);
  assert.match(ui,/request_id:id/);
  assert.match(ui,/account_id:account/);
  assert.match(ui,/result\.request_id!==id/);
  assert.match(ui,/result\.account_id!==account/);
  assert.doesNotMatch(ui,/@html|innerHTML|localStorage/);
});

test('integration exercises real group creation, exact site and account linking, retries, revocations',()=>{
  for(const name of ['AdminNativeCoachRegistrationAsync','SeedAnnouncementTerritoriesAsync',
    'VerifyNativeCoachAsync','CountNativeCoachAuditAsync','CleanupNativeCoachesAsync',
    'native_coach_denied','native_coach_group_unassigned','native_coach_conflict',
    'replayed','not_authenticated','not_authorized'])
    assert.ok(fixture.includes(name),'Missing native Coach proof '+name);
});
