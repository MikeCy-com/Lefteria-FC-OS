import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115960000_GroupProfileExactTerritoryBoundary.cs');
const academyApi=read('backend/src/LefteriaFc.Api/Audiences/AdminAcademyGroupProfileEndpoints.cs');
const teamApi=read('backend/src/LefteriaFc.Api/Audiences/AdminFirstTeamProfileEndpoints.cs');

test('all six group profile and publicity functions enforce current scoped authority',()=>{
  assert.match(migration,/CREATE FUNCTION club\.require_admin_group_profile_scope\(/);
  assert.match(migration,/club\.authorize_admin_football\(_digest,_owner\)/);
  assert.match(migration,/club\.admin_football_allowed\(/);
  assert.match(migration,/JOIN club\.football_cities c[\s\S]*c\.is_active/);
  assert.match(migration,/JOIN club\.football_locations l[\s\S]*l\.is_active/);
  assert.match(migration,/FOR SHARE OF a,s,c,l/);
  assert.match(migration,/v_scope,v_location,_write/);
  for(const [name,owner,entity,permissions] of [
    ['read_admin_first_team_profile','lefteria_fc','_team','false'],
    ['write_admin_first_team_profile','lefteria_fc','_team','true'],
    ['read_admin_academy_group_profile','lefteria_fc_academy','_group','false'],
    ['write_admin_academy_group_profile','lefteria_fc_academy','_group','true'],
    ['read_admin_academy_group_publicity','lefteria_fc_academy','_group','false'],
    ['set_admin_academy_group_publicity','lefteria_fc_academy','_group','true']
  ]){
    const marker="PERFORM club.require_admin_group_profile_scope(_digest,'"+
      owner+"',"+entity+","+permissions+");";
    const idx=migration.indexOf('CREATE OR REPLACE FUNCTION club.'+name+'(');
    assert.ok(idx!==-1,'missing SQL function '+name);
    const next=migration.indexOf('CREATE OR REPLACE FUNCTION club.',idx+20);
    const body=migration.slice(idx,next<0?undefined:next);
    assert.ok(body.includes(marker),'missing exact delegation for '+name);
  }
});

test('Super Admin only sees unmapped legacy groups for reconciliation; inactive assigned sites deny',()=>{
  assert.match(migration,/v_role='super_admin' THEN RETURN v_actor/);
  assert.match(migration,/v_role='super_admin' AND NOT EXISTS\(/);
  assert.match(migration,/academy_profile_active_site_required/);
  assert.match(migration,/academy_profile_location_denied/);
  assert.match(migration,/first_team_profile_scope_denied/);
  assert.match(migration,/REVOKE ALL ON FUNCTION club\.require_admin_group_profile_scope/);
  assert.doesNotMatch(migration,/GRANT EXECUTE|GRANT SELECT|UPDATE club\.players|DELETE FROM club\.players/);
});

test('admin Academy and First Team profile APIs still reject forbidden DB writes with 403',()=>{
  assert.match(academyApi,/when\(ex\.SqlState is "42501"\)/);
  assert.match(academyApi,/403,"academy_profile_denied"/);
  assert.match(teamApi,/when\(ex\.SqlState is "42501"\)/);
  assert.match(teamApi,/403,"team_profile_denied"/);
  assert.match(academyApi,/club\.set_admin_academy_group_publicity/);
});
