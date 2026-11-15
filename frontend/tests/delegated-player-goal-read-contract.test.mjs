import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115920000_DelegatedPlayerGoalRead.cs');
const original=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115900000_PlayerDevelopmentGoals.cs');
const api=load('backend/src/LefteriaFc.Api/Evaluations/AdminPlayerDevelopmentGoalsEndpoints.cs');
const grants=load('ops/postgres/player-development-goals-admin-grants.sql');
const allowlist=load('ops/postgres/web-admin-auth-permissions-check.sql');

test('delegated read is only a new SECURITY DEFINER function; originals remain super-admin-only',()=>{
  assert.match(migration,/CREATE FUNCTION club\.read_admin_player_development_goals_manager/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/REVOKE ALL ON FUNCTION[\s\S]*read_admin_player_development_goals_manager/);
  assert.doesNotMatch(migration,/GRANT EXECUTE|CREATE FUNCTION club\.create_admin_player|CREATE FUNCTION club\.update_admin_player/);
  assert.match(original,/v_role<>'super_admin'/);
});
test('territory checks require live unique canonical membership AND creation-time snapshot',()=>{
  for(const rule of [
    'v_mapped<>v_total','v_scope_count<>1','v_city_count<>1','v_location_count<>1',
    'g.origin_scope_id=v_scope','g.origin_city_id=v_city','g.origin_location_id=v_location',
    "g.role='location_manager'","g.role='city_manager'","g.role='team_manager'",
    'FOR SHARE','p.is_active AND NOT p.is_archived AND NOT p.is_transferred'
  ])assert.ok(migration.includes(rule),rule);
  assert.match(migration,/'source_evaluation_id',NULL/);
  assert.doesNotMatch(migration,/g\.origin_scope_id IS NULL\s+OR/);
});
test('read-only manager API, exact grant and admin-role allowlist; no manager writes',()=>{
  assert.match(api,/managerRead\s*=\s*session\?\.User\.Role == "manager"/);
  assert.match(api,/read_admin_player_development_goals_manager/);
  assert.match(api,/write \|\| session\.User\.Role != "manager"/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_admin_player_development_goals_manager/);
  assert.match(allowlist,/club\.read_admin_player_development_goals_manager\(text,text,text\)'::regprocedure/);
});
