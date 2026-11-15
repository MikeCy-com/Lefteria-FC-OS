import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115950000_ScopedFootballGroupCreation.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminProfiledGroupCreateEndpoints.cs');
const page=read('frontend/src/routes/admin/groups/+page.svelte');
const grants=read('ops/postgres/admin-profiled-group-create-grants.sql');

test('scoped group creation is a single owner-locked, auditable transaction',()=>{
  for(const source of ['club.authorize_admin_football','club.admin_football_allowed',
    'club.football_locations','club.football_cities','club.football_operational_scopes',
    'club.academy_group_location_assignments',
    'club.create_admin_academy_location_group_with_profile',
    'club.write_admin_academy_group_profile',
    'club.admin_group_create_requests','ON CONFLICT(organization_id,actor_id,request_id)',
    'input_sha256<>v_hash','location_id',"'verified_location'",
    'pg_advisory_xact_lock','v_role<>','REVOKE ALL ON FUNCTION'])
    assert.ok(migration.includes(source),'Missing scoped guard: '+source);
  assert.match(migration,/v_role<>'super_admin'/);
  assert.match(migration,/academy_group_location_required/);
  assert.match(migration,/INSERT INTO club\.football_operational_scopes\(organization_id,team_id\)/);
  assert.match(migration,/FOR SHARE OF l,c,s/);
  assert.match(migration,/l\.is_active/);
  assert.match(migration,/c\.is_active/);
  assert.doesNotMatch(migration,/UPDATE club\.players|DELETE FROM club\.players|sendSms|push_notification/);
});

test('strict API requires exact Academy location and rejects manager-created flat First Team',()=>{
  assert.match(api,/JsonUnmappedMemberHandling.Disallow/);
  assert.match(api,/ValidateUniqueKeys\(doc.RootElement\)/);
  assert.match(api,/request\.LocationId is null \|\| request\.LocationId==Guid\.Empty/);
  assert.match(api,/organization_id=="lefteria_fc" && session\.User\.Role!="super_admin"/);
  assert.match(api,/create_admin_academy_location_group_with_profile/);
  assert.match(api,/command\.Parameters\.AddWithValue\("location",request\.LocationId!\.Value\)/);
  assert.match(api,/root\.TryGetProperty\("location_id"/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.create_admin_academy_location_group_with_profile/);
});

test('Academy creation UI lists only verified active writable sites and never trusts URL alone',()=>{
  assert.ok(compile(page,{filename:'groups/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(page,/data-testid="admin-group-verified-location"/);
  assert.match(page,/fetch\('\/api\/admin\/football\/territories\?organization_id=lefteria_fc_academy'/);
  assert.match(page,/row\.is_active!==true \|\| row\.can_manage!==true/);
  assert.match(page,/scopedCities\.has\(row\.city_id\)/);
  assert.match(page,/creationLocations\.find\(item=>item\.id===creationLocationId\)/);
  assert.match(page,/location_id:selectedLocation\.id/);
  assert.match(page,/value\.location_id!==selectedLocation\.id/);
  assert.match(page,/data-testid="admin-verified-group-location"/);
  assert.match(page,/organization !== 'lefteria_fc' \|\| \$auth\.session\.user\.role==='super_admin'/);
  assert.doesNotMatch(page,/localStorage|sessionStorage|Math\.random/);
});
