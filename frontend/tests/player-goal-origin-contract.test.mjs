import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115910000_PlayerGoalTerritoryOrigin.cs');
const sql=load('ops/postgres/player-goal-origin-checks.sql');
const policy=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115900000_PlayerDevelopmentGoals.cs');
const api=load('backend/src/LefteriaFc.Api/Evaluations/AdminPlayerDevelopmentGoalsEndpoints.cs');
const ui=load('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
const ci=load('.github/workflows/rewrite-build.yml');

test('creation-time goal origin is nullable, verified and immutable',()=>{
  assert.match(migration,/ADD COLUMN origin_scope_id uuid/);
  assert.match(migration,/ADD COLUMN origin_city_id uuid/);
  assert.match(migration,/ADD COLUMN origin_location_id uuid/);
  assert.match(migration,/ADD COLUMN origin_group_id varchar/);
  assert.match(migration,/origin_recorded_at/);
  assert.match(migration,/BEFORE INSERT ON club\.player_development_goals/);
  assert.match(migration,/BEFORE UPDATE ON club\.player_development_goals/);
  assert.match(migration,/goal_origin_is_immutable/);
  assert.match(migration,/goal_origin_is_server_set/);
  assert.match(migration,/REVOKE ALL ON FUNCTION club\.capture_player_goal_origin/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/FOREIGN KEY\(origin_city_id,origin_location_id\)/);
  assert.match(migration,/FOREIGN KEY\(organization_id,origin_group_id\)/);
  assert.match(migration,/count\(\*\) FILTER\(WHERE ag\.id IS NOT NULL/);
  assert.match(migration,/v_total=v_mapped AND v_scopes=1 AND v_cities=1/);
  assert.match(migration,/v_locations=1/);
  assert.match(migration,/UNION/);
  assert.doesNotMatch(migration,/UPDATE club\.player_development_goals SET origin_/);
});
test('actual disposable PostgreSQL tests cover transfers and ambiguity',()=>{
  for(const text of [
    'single_group_origin_incorrect','same_site_multigroup_origin_incorrect',
    'ambiguous_or_unmapped_goal_origin_inferred','first_team_origin_incorrect',
    'transfer_rewrote_historical_origin','group_relocation_changed_goal_origin',
    'goal_origin_update_was_allowed',
    'goal_origin_spoof_was_allowed'
  ])assert.ok(sql.includes(text),text);
  assert.match(sql,/BEGIN;[\s\S]*ROLLBACK;/);
  assert.match(ci,/player-goal-origin-checks\.sql/);
});
test('delegated private goal access remains unchanged while origin matures',()=>{
  assert.match(policy,/v_role<>'super_admin'/);
  assert.match(api,/session\.User\.Role != "super_admin"/);
  assert.match(ui,/allowGoalManagement=\{user\.role==='super_admin'\}/);
  assert.doesNotMatch(migration,/GRANT EXECUTE/);
});
