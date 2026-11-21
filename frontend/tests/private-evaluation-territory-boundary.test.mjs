import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115970000_PrivateEvaluationTerritoryBoundary.cs');
const fixture=load('ops/postgres/football-territory-delegation-checks.sql');
const runner=load('.github/workflows/rewrite-build.yml');
const api=load('backend/src/LefteriaFc.Api/Evaluations/AdminAcademyEvaluationEndpoints.cs');

test('private assessment origin is immutable, nullable and never inferred from transfers',()=>{
  for(const symbol of ['origin_scope_id','origin_city_id','origin_location_id',
    'origin_recorded_at','ck_evaluation_origin_complete','tr_evaluation_origin_capture',
    'tr_evaluation_origin_immutable','evaluation_origin_server_only',
    'evaluation_origin_immutable','FOREIGN KEY (origin_city_id,origin_location_id)'])
    assert.ok(migration.includes(symbol),symbol);
  assert.match(migration,/NEW\.origin_recorded_at:=clock_timestamp\(\)/);
  assert.match(migration,/REVOKE ALL ON FUNCTION academy_private\.capture_evaluation_origin\(\) FROM PUBLIC/);
});
test('exact manager grants restrict reads and creation at database boundary',()=>{
  for(const clause of [
    "a.role='manager'","g.role='team_manager'","g.role='city_manager'",
    "g.role='location_manager'","v_total=v_mapped","v_scopes=1",
    "v_cities=1","v_locations=1","FOR SHARE",
    "v_role='super_admin' OR","e.origin_scope_id=v_scope",
    'e.origin_city_id=v_city','e.origin_location_id=v_location',
    'PERFORM 1 FROM academy_private.authorize_private_player_manager'
  ])assert.ok(migration.includes(clause),clause);
  assert.match(migration,/CREATE OR REPLACE FUNCTION academy_private\.read_admin_player_evaluations/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION academy_private\.create_admin_player_evaluation/);
  assert.match(api,/academy_private\.read_admin_player_evaluations/);
  assert.match(api,/academy_private\.create_admin_player_evaluation/);
});
test('disposable PostgreSQL suite exercises manager permissions and transfer',()=>{
  for(const id of ['evaluation_manager_create_unverified',
    'manager_wrote_sibling_evaluation','manager_read_sibling_evaluation',
    'city_manager_cannot_read_own_assessment','evaluation_origin_not_captured',
    'evaluation_origin_overwritten','old_city_manager_read_transferred_assessment',
    'new_city_manager_inherited_old_assessment'])
    assert.ok(fixture.includes(id),id);
  assert.match(fixture,/BEGIN;[\s\S]*ROLLBACK;/);
  assert.match(runner,/football-territory-delegation-checks\.sql/);
});
