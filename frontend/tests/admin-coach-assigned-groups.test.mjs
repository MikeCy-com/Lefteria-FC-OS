import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {adminSectionsFor} from '../src/lib/admin/module-catalog.ts';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const catalog=read('frontend/src/lib/admin/module-catalog.ts');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
const page=read('frontend/src/routes/admin/assigned-groups/+page.svelte');
const endpoint=read('backend/src/LefteriaFc.Api/Audiences/AdminAudienceEndpoints.cs');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261124121500_CoachAssignedGroupDirectory.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const checks=read('ops/postgres/web-admin-auth-permissions-check.sql');
const native=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminHttpChecks.NativeCoach.cs');
const owner=(id)=>({id,name:id});
const actor=(role,orgs,permissions)=>({
  id:'test-admin',role,name:'Test',username:'tester',
  organizations:orgs.map(owner),permissions
});
const modules=u=>adminSectionsFor(u).flatMap(s=>s.modules);

test('only assigned-capability coaches with real owner grants can enter coach directory',()=>{
  const coach=actor('coach',['lefteria_fc_academy'],['academy:read_assigned']);
  const mod=modules(coach).find(m=>m.id==='assigned_groups');
  assert.equal(mod?.status,'live');
  assert.equal(mod?.href,'/admin/assigned-groups');
  assert.ok(!modules(coach).some(m=>m.id==='groups'||m.id==='registrations'));
  assert.ok(!modules(actor('coach',['lefteria_fc'],['academy:read_assigned']))
    .some(m=>m.id==='assigned_groups'));
  assert.ok(!modules(actor('editor',['lefteria_fc_academy'],['academy:read_assigned']))
    .some(m=>m.id==='assigned_groups'));
  assert.ok(!modules(actor('manager',['lefteria_fc_academy'],['academy:read_assigned']))
    .some(m=>m.id==='assigned_groups'));
  assert.ok(!modules(actor('coach',[],['academy:read_assigned']))
    .some(m=>m.id==='assigned_groups'));
  assert.match(catalog,/user\.role==='coach'/);
});

test('coach UI compiles and only displays validated current-owner group IDs and names',()=>{
  assert.ok(compile(frame,{filename:'AdminFrame.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(page,{filename:'assigned-groups/+page.svelte',generate:'server'}).js.code.length>100);
  for(const phrase of [
    'data-testid="admin-coach-assigned-groups-link"',
    'href="/admin/assigned-groups"',
    "active==='assigned-groups'?'page':undefined"
  ])assert.ok(frame.includes(phrase),phrase);
  for(const phrase of [
    'data-testid="admin-coach-assigned-directory"',
    'data-testid="coach-assigned-list"',
    'data-testid="coach-assigned-empty"',
    'data-testid="coach-assigned-unavailable"',
    'data-testid="coach-assigned-denied"',
    "hasAdminPermission($auth.session!.user.permissions,'academy:read_assigned')",
    "hasAdminPermission($auth.session!.user.permissions,'teams:read_assigned')",
    "if(assignedOwners.some(org=>org.id===next))owner=next as Owner;",
    "if(!validGroups(data,selected))",
    "$auth.session?.user.id===actor",
    "credentials:'same-origin',cache:'no-store'",
    "auth.invalidateSession()",
    "void auth.bootstrap()"
  ])assert.ok(page.includes(phrase),phrase);
  assert.doesNotMatch(page,/\/api\/admin\/football\/audiences|active_players|player_id|localStorage/);
});

test('server preserves broad Coach denial while permitting locked assigned-only GET',()=>{
  assert.match(endpoint,/GetAdminAudienceDirectory/);
  assert.match(endpoint,/GetAdminCoachAssignedGroups/);
  assert.match(endpoint,/session\.User\.Role!="coach"/);
  assert.match(endpoint,/teams:read_assigned/);
  assert.match(endpoint,/academy:read_assigned/);
  assert.match(endpoint,/session\.User\.Organizations\.Any\(org=>org\.Id==organization_id\)/);
  assert.match(endpoint,/context\.Request\.Query\.Count!=1/);
  assert.match(endpoint,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role IS DISTINCT FROM 'coach'/);
  assert.match(sql,/identity\.admin_staff_links/);
  assert.match(sql,/st\.is_active AND NOT st\.is_archived/);
  assert.match(sql,/staff_teams/);
  assert.match(sql,/staff_academy_groups/);
  assert.match(sql,/academy_group_coaches/);
  assert.match(sql,/UNION/);
  assert.doesNotMatch(sql,/active_players|guardian_phone|birth_date|FROM club\.players|FROM identity\.mobile_identities/);
});

test('DB function is REVOKED from mobile/public and allowlisted only for web admin execution',()=>{
  const sig='club.read_admin_coach_assigned_groups(text,text)';
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
  assert.ok(grants.includes("'"+sig+"'::regprocedure"));
  assert.ok(checks.includes("'"+sig+"'::regprocedure"));
  assert.match(native,/assigned-group projection exposed another team or private roster data/);
  assert.match(native,/Unlinked Academy Coach received inferred private group assignments/);
  assert.match(native,/Academy Coach discovered a sibling location or private membership counts/);
  assert.match(native,/RevokeOrganizationAsync\(coachAccount,fc\)/);
  assert.match(native,/ExpectJsonAsync\(expired,401,"not_authenticated"\)/);
});
