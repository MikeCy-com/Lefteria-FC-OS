import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110172000_AtomicProfiledTeamCreation.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminProfiledGroupCreateEndpoints.cs');
const app=read('backend/src/LefteriaFc.Api/Program.cs');
const ui=read('frontend/src/routes/admin/groups/+page.svelte');
const grants=read('ops/postgres/admin-profiled-group-create-grants.sql');

test('atomic group creation uses original audited canonical writer and profile in one transaction',()=>{
  assert.match(sql,/v_saved:=club.create_admin_group\(_digest,_owner,_id,_name\)/);
  assert.match(sql,/club.write_admin_first_team_profile\(/);
  assert.match(sql,/club.write_admin_academy_group_profile\(/);
  assert.match(sql,/ON CONFLICT\(organization_id,actor_id,request_id\) DO NOTHING/);
  assert.match(sql,/CREATE TABLE club.admin_group_create_requests/);
  assert.match(sql,/UNIQUE\(organization_id,group_id\)/);
  assert.match(sql,/ALTER TABLE club.admin_group_create_requests ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(sql,/GRANT EXECUTE|GRANT SELECT|UPDATE club.players|DELETE FROM club.player_academy_groups/);
});
test('retry requires current manager grant and same content digest',()=>{
  assert.match(sql,/club.authorize_admin_first_team_profile\(_digest\)/);
  assert.match(sql,/club.authorize_admin_academy_profile\(_digest\)/);
  assert.match(sql,/jsonb_build_object\('name',_name,'profile',_profile\)/);
  assert.match(sql,/input_sha256<>v_hash/);
  assert.match(sql,/profiled_group_replay_mismatch/);
  assert.match(sql,/v_existing.group_id/);
  assert.match(sql,/profiled_group_replay_inconsistent/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club.create_admin_group_with_profile/);
});
test('strict manager endpoint only accepts one exact organization and profile type',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role,permission\)/);
  assert.match(api,/context.Request.Query\["organization_id"\].Count != 1/);
  assert.match(api,/JsonUnmappedMemberHandling.Disallow/);
  assert.match(api,/ValidateUniqueKeys\(doc.RootElement\)/);
  assert.match(api,/request.TeamProfile is null \|\| request.AcademyProfile is not null/);
  assert.match(api,/request.AcademyProfile is null \|\| request.TeamProfile is not null/);
  assert.match(api,/NpgsqlDbType.Jsonb/);
  assert.match(app,/app.MapAdminProfiledGroupCreates\(\)/);
});
test('original First Team and Academy full profile fields are included in creation UI',()=>{
  assert.match(ui,/groups\/with-profile\?/);
  assert.match(ui,/team_profile:\{level:teamLevel,description:teamDescription,banner_url:teamBanner\}/);
  assert.match(ui,/academy_profile:\{age_range:academyAge,coach_name:academyCoach/);
  for(const k of ['training_schedule:academySchedule','max_players:academyCapacity',
    'season:academySeason','banner_url:academyBanner','display_order:academyOrder'])
    assert.ok(ui.includes(k),'missing original Academy field '+k);
  assert.match(ui,/crypto.randomUUID\(\)/);
  assert.match(ui,/request_id:requestId/);
  assert.match(ui,/value.profile/);
  assert.match(ui,/value.profile as Record<string,unknown>\).revision/);
  assert.match(ui,/oninput=\{\(\) => createRequestId=''\}/);
  assert.match(ui,/\/\[\\x00-\\x1f\\x7f\]\//);
});
test('SQL grants are review-only and never grant direct roster or retry-table writes',()=>{
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club.create_admin_group_with_profile/);
  assert.match(grants,/profiled_group_role_boundary_invalid/);
  assert.match(grants,/profiled_group_grant_scope_invalid/);
  assert.doesNotMatch(grants,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON/);
});