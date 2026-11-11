import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110171000_FirstTeamProfileEditor.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminFirstTeamProfileEndpoints.cs');
const app=read('backend/src/LefteriaFc.Api/Program.cs');
const page=read('frontend/src/routes/admin/groups/+page.svelte');
const ui=read('frontend/src/lib/admin/AdminFirstTeamProfilePanel.svelte');
const grant=read('ops/postgres/admin-first-team-profile-grants.sql');

test('First Team source fields level, description, banner are in database, API and UI',()=>{
  for(const f of ['level','description','banner_url']){
    assert.match(sql,new RegExp(f));
    assert.ok(api.includes('JsonPropertyName("'+f+'")'));
    assert.ok(ui.includes(f));
  }
  assert.match(api,/JsonPropertyName\("expected_revision"\)/);
  assert.match(app,/app.MapAdminFirstTeamProfiles\(\)/);
  assert.match(page,/AdminFirstTeamProfilePanel/);
  assert.match(page,/organization === 'lefteria_fc'/);
});
test('source metadata retains canonical team ID and independent roster/staff',()=>{
  assert.match(sql,/REFERENCES club.teams\(organization_id,id\)/);
  assert.match(sql,/PRIMARY KEY\(organization_id,team_id\)/);
  assert.doesNotMatch(sql,/UPDATE club.players|DELETE FROM club.players/);
  assert.doesNotMatch(sql,/UPDATE club.staff|DELETE FROM club.staff/);
});
test('manager current organization grant and private RLS gates every source profile',()=>{
  assert.match(sql,/identity.lock_admin_club_authority\(_digest,'lefteria_fc'\)/);
  assert.match(sql,/admin_organization_grants g/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/s.revoked_at IS NULL/);
  assert.match(sql,/ENABLE ROW LEVEL SECURITY/);
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role,"teams:write"\)/);
  assert.match(api,/context.Request.Query\["organization_id"\].Count!=1/);
  assert.match(grant,/team_profile_grant_scope_invalid/);
  assert.doesNotMatch(grant,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON/);
});
test('optimistic revision and audit protect update from stale concurrent admin writes',()=>{
  assert.match(sql,/team_profile_revision_conflict/);
  assert.match(sql,/coalesce\(v_old,0\)<>_expected/);
  assert.match(sql,/v_next:=_expected\+1/);
  assert.match(sql,/audit.security_events/);
  assert.match(ui,/expected_revision:revision/);
  assert.match(ui,/raw.revision!==revision\+1/);
  assert.match(ui,/response.status===409/);
});
test('source First Team editor is safe on credential revocation and malformed API data',()=>{
  assert.match(ui,/response.status===401/);
  assert.match(ui,/response.status===403/);
  assert.match(ui,/isValid\(raw,selected\)/);
  assert.match(ui,/controller.abort\(\)/);
  assert.match(ui,/cache:'no-store'/);
  assert.doesNotMatch(ui,/localStorage|sessionStorage|\{@html/);
});