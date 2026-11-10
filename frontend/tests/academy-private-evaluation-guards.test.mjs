import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const model=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110100000_AcademyPrivateEvaluationsDocuments.cs');
const guards=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110110000_AcademyPrivateRevisionGuards.cs');
const probes=read('ops/postgres/academy-private-evaluation-guards-checks.sql');

test('evaluation snapshot revisions cannot be rewritten or deleted',()=>{
  assert.match(guards,/BEFORE UPDATE OR DELETE ON academy_private\.evaluation_revisions/);
  assert.match(guards,/BEFORE UPDATE OR DELETE ON academy_private\.document_audit_events/);
  assert.match(guards,/RAISE EXCEPTION 'academy_private_history_immutable'/);
  assert.match(guards,/BEFORE INSERT ON academy_private\.evaluation_revisions/);
});
test('revision insert locks the exact parent and rejects stale, foreign or expired editors',()=>{
  assert.match(guards,/WHERE id=NEW\.evaluation_id FOR UPDATE/);
  assert.match(guards,/NEW\.editor_id<>v_parent\.author_id/);
  assert.match(guards,/clock_timestamp\(\)>=v_parent\.edit_until/);
  assert.match(guards,/NEW\.revision<>v_parent\.current_revision\+1/);
  assert.match(guards,/NEW\.edited_at := clock_timestamp\(\)/);
  assert.match(guards,/SET current_revision=NEW\.revision/);
});
test('Academy score groups are validated inside PostgreSQL',()=>{
  for(const section of ['technical','tactical','physical','character'])
    assert.match(guards,new RegExp("validate_academy_score_group\\(NEW\\."+section+",'"+section+"'\\)"));
  for(const key of ['passing','first_touch','positioning','decision_making',
    'speed','coordination','respect','resilience'])assert.ok(guards.includes("'"+key+"'"));
  assert.match(guards,/jsonb_typeof\(v_score\)<>'number'/);
  assert.match(guards,/v_num<0 OR v_num>100 OR v_num<>trunc\(v_num\)/);
  assert.doesNotMatch(guards,/GRANT SELECT|GRANT EXECUTE|CREATE POLICY/);
  assert.match(model,/edit_until=created_at\+interval '24 hours'/);
});
test('SQL scenario is rollback-only, includes invalid scores and denied public access',()=>{
  assert.match(probes,/BEGIN;/);
  assert.match(probes,/ROLLBACK;/);
  assert.match(probes,/invalid_score_was_accepted/);
  assert.match(probes,/fractional_score_was_accepted/);
  assert.match(probes,/missing_private_evaluation_rls/);
  assert.match(probes,/private_evaluations_publicly_granted/);
});
