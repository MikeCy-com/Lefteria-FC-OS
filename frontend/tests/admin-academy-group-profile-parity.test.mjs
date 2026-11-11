import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const db=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110170000_AcademyGroupProfileEditor.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminAcademyGroupProfileEndpoints.cs');
const app=read('backend/src/LefteriaFc.Api/Program.cs');
const ui=read('frontend/src/lib/admin/AdminAcademyGroupProfilePanel.svelte');
const groupPage=read('frontend/src/routes/admin/groups/+page.svelte');
const grant=read('ops/postgres/admin-academy-profile-grants.sql');
const expected=['age_range','coach_name','training_schedule','description',
 'max_players','season','banner_url','display_order'];

test('Original Academy group creation fields have exact snake_case contract end-to-end',()=>{
  for(const f of expected){
    assert.ok(db.includes(f),'SQL missing '+f);
    assert.ok(api.includes('JsonPropertyName("'+f+'")'),'API missing '+f);
    assert.ok(ui.includes(f),'UI JSON missing '+f);
  }
  assert.match(api,/MapAdminAcademyGroupProfiles/);
  assert.match(app,/app.MapAdminAcademyGroupProfiles\(\)/);
  assert.match(groupPage,/AdminAcademyGroupProfilePanel/);
  assert.match(groupPage,/organization === 'lefteria_fc_academy'/);
});
test('profile creation preserves canonical roster IDs, memberships and independent coach assignments',()=>{
  assert.match(db,/REFERENCES club.academy_groups\(organization_id,id\)/);
  assert.match(db,/club.player_academy_groups/);
  assert.match(db,/v_enrolled>v_data.max_players/);
  assert.doesNotMatch(db,/INSERT INTO club.academy_groups|UPDATE club.players|UPDATE club.staff/);
  assert.doesNotMatch(api,/coach-assignments|\/api\/admin\/football\/assignments/);
  assert.match(ui,/Προπονητής \(όνομα εμφάνισης\)/);
});
test('Academy group profile cannot be accessed without current organization manager grant',()=>{
  assert.match(db,/identity.lock_admin_club_authority\(_digest,'lefteria_fc_academy'\)/);
  assert.match(db,/admin_organization_grants g/);
  assert.match(db,/s.revoked_at IS NULL/);
  assert.match(db,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(api,/RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role,"academy:write"\)/);
  assert.match(api,/session.User.Organizations.Any/);
  assert.match(api,/context.Request.Query\["organization_id"\].Count!=1/);
  assert.match(db,/ENABLE ROW LEVEL SECURITY/);
});
test('updates use revision conflict control, audit trail and hard capacity ceiling',()=>{
  assert.match(db,/v_previous,0\)<>_expected/);
  assert.match(db,/academy_profile_revision_conflict/);
  assert.match(db,/v_revision:=_expected\+1/);
  assert.match(db,/v_data.max_players NOT BETWEEN 1 AND 200/);
  assert.match(db,/academy_profile_capacity_below_roster/);
  assert.match(db,/audit.security_events/);
  assert.match(ui,/expected_revision:revision/);
  assert.match(ui,/raw.revision!==revision\+1/);
  assert.match(ui,/response.status===409/);
});
test('read and write endpoints cannot leak existing children or bypass private SQL grant',()=>{
  assert.match(api,/club.read_admin_academy_group_profile/);
  assert.match(api,/club.write_admin_academy_group_profile/);
  assert.match(db,/REVOKE ALL ON FUNCTION club.read_admin_academy_group_profile/);
  assert.match(db,/REVOKE ALL ON FUNCTION club.write_admin_academy_group_profile/);
  assert.match(grant,/academy_profile_public_function_detected/);
  assert.match(grant,/academy_profile_grant_scope_failed/);
  assert.match(grant,/has_table_privilege/);
  assert.doesNotMatch(grant,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON/);
  assert.doesNotMatch(db,/UPDATE club.player_academy_groups|DELETE FROM club.player_academy_groups/);
});
test('Academy metadata editor handles revoked session, invalid responses and no invented success',()=>{
  assert.match(ui,/response.status===401/);
  assert.match(ui,/response.status===403/);
  assert.match(ui,/valid\(raw,id\)/);
  assert.match(ui,/cache:'no-store',credentials:'same-origin'/);
  assert.match(ui,/controller.abort\(\)/);
  assert.match(ui,/succeeded|saved='Οι πληροφορίες του τμήματος αποθηκεύτηκαν\.'/);
  assert.doesNotMatch(ui,/localStorage|sessionStorage|\{@html/);
});