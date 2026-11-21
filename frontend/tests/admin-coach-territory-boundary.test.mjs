import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115980000_CoachTerritoryBoundary.cs');
const baseRead=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108160000_AdminCoachDirectory.cs');
const baseWrite=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108180000_AdminCoachAssignment.cs');
const integration=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminHttpChecks.CoachTerritory.cs');

test('versioned coach reader overrides legacy owner-wide query with exact authorized group projection',()=>{
  assert.match(baseRead,/CREATE FUNCTION club\.read_admin_coaches/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION club\.read_admin_coaches/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION club\.write_admin_coach_assignment/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(migration,/club\.football_team_admin_grants|club\.admin_football_allowed/);
  assert.match(migration,/club\.academy_group_location_assignments/);
  assert.match(migration,/club\.admin_football_allowed\([\s\S]*?false/);
  assert.match(migration,/v_role='super_admin'/);
  assert.match(migration,/v_role='manager' AND EXISTS/);
  assert.match(migration,/FROM permitted_groups g WHERE g\.staff_id=p\.id/);
  assert.match(migration,/SELECT count\(\*\)::integer FROM eligible_staff/);
  assert.doesNotMatch(migration,/normalized_phone|parent_phone|guardian_phone/);
});

test('manager coach writes check both existing and requested memberships before CAS and mutation',()=>{
  assert.match(baseWrite,/CREATE FUNCTION club\.write_admin_coach_assignment/);
  assert.match(migration,/jsonb_array_length\(v_current\)=0/);
  assert.match(migration,/jsonb_array_elements_text\(v_current\)/);
  assert.match(migration,/jsonb_array_elements_text\(v_desired\)/);
  assert.match(migration,/club\.admin_football_allowed\([\s\S]*?true/);
  assert.match(migration,/coach_assignment_territory_denied/);
  const denied=migration.indexOf('coach_assignment_territory_denied');
  const conflict=migration.lastIndexOf("IF v_current<>v_expected THEN");
  const firstWrite=migration.indexOf("DELETE FROM club.staff_teams");
  assert.ok(denied>0&&denied<conflict&&conflict<firstWrite);
  assert.match(migration,/admin\.coach\.groups\.replace/);
  assert.doesNotMatch(migration,/GRANT SELECT ON club\.staff|GRANT UPDATE ON club\.staff/);
});

test('rollback is fail-closed and isolated integration proves separate city, site and squad rights',()=>{
  assert.match(migration,/coach_directory_upgrade_required/);
  assert.match(migration,/coach_assignment_upgrade_required/);
  assert.match(integration,/SeedAnnouncementTerritoriesAsync/);
  assert.match(integration,/GrantAnnouncementCityAsync/);
  assert.match(integration,/RevokeAnnouncementTerritoriesAsync/);
  assert.match(integration,/coach.Coach1|coaches.Coach1/);
  assert.match(integration,/roster\.Group1/);
  assert.match(integration,/roster\.Group2/);
  assert.match(integration,/roster\.Team1/);
  assert.match(integration,/roster\.Team2/);
  assert.match(integration,/403,"not_authorized"/);
});
