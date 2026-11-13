import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const migration=readFileSync(new URL('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113090000_AcademyTrainingPlanDraftCollisions.cs',import.meta.url),'utf8');
const panel=readFileSync(new URL('../src/lib/admin/AdminAcademyTrainingPlans.svelte',import.meta.url),'utf8');
const lifecycle=readFileSync(new URL('../../ops/postgres/academy-training-plans-lifecycle-checks.sql',import.meta.url),'utf8');
const grants=readFileSync(new URL('../../ops/postgres/academy-training-plans-admin-grants.sql',import.meta.url),'utf8');
test('draft conflicts are serialized in the database, not guessed in UI',()=>{
  assert.match(migration,/BEFORE INSERT OR UPDATE OF/);
  assert.match(migration,/pg_advisory_xact_lock/);
  assert.match(migration,/NEW\.organization_id\|\|':'\|\|NEW\.group_id/);
  assert.match(migration,/AND p\.status='draft' AND p\.id<>NEW\.id/);
  assert.match(migration,/AND \(p\.training_day\+p\.start_time\)<v_end/);
  assert.match(migration,/training_plan_group_time_overlap/);
  assert.match(migration,/USING ERRCODE='23505'/);
  assert.match(migration,/training_plan_cannot_span_midnight/);
  assert.match(migration,/IF NEW\.status<>'draft' THEN RETURN NEW/);
  assert.doesNotMatch(migration,/INSERT INTO club\.calendar_items|UPDATE club\.calendar_items|INSERT INTO club\.calendar_responses/);
});
test('disposable postgres probe covers conflict and the archived-slot release',()=>{
  assert.match(lifecycle,/Overlapping Academy draft was accepted/);
  assert.match(lifecycle,/Overnight training draft was accepted/);
  assert.match(lifecycle,/Archived plan still blocks unrelated new draft/);
  assert.match(lifecycle,/EXCEPTION WHEN unique_violation THEN NULL/);
  assert.match(lifecycle,/ROLLBACK;/);
  assert.match(grants,/training_plans_collision_trigger_exposed/);
  assert.match(panel,/Η ώρα συγκρούεται με άλλο ενεργό πρόχειρο/);
  assert.match(migration,/REVOKE ALL ON FUNCTION club\.reject_academy_training_plan_overlap\(\) FROM PUBLIC/);
});
