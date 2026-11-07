import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL(
  '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108001500_FirstTeamTrialsDisplayProjection.cs',
  import.meta.url),'utf8');
const integration=readFileSync(new URL(
  '../../backend/tests/LefteriaFc.Identity.IntegrationTests/TestDatabase.FirstTeamTrials.cs',
  import.meta.url),'utf8');

test('private first-team display selects only original club scope and remains closed',()=>{
  assert.ok(source.includes('CREATE FUNCTION club.read_first_team_trial_display()'));
  assert.ok(source.includes('SECURITY DEFINER'));
  assert.ok(source.includes('SET search_path=pg_catalog,pg_temp'));
  assert.ok(source.includes('LEFT JOIN club.first_team_trial_settings AS settings'));
  assert.ok(source.includes("settings.organization_id='lefteria_fc'"));
  assert.ok(source.includes("REVOKE ALL ON FUNCTION club.read_first_team_trial_display() FROM PUBLIC"));
  assert.ok(source.includes('false,'));
  assert.ok(source.includes('προσωρινά κλειστές'));
  assert.doesNotMatch(source,/^\s*GRANT\s+EXECUTE|^\s*CREATE POLICY|^\s*UPDATE\s|^\s*INSERT\s/im);
});

test('actual PostgreSQL test checks closed default and narrow app read permission',()=>{
  assert.ok(integration.includes('AssertFirstTeamTrialDisplayProjectionClosedAsync'));
  assert.ok(integration.includes('!rows.GetBoolean(0)'));
  assert.ok(integration.includes("has_function_privilege(current_user,"));
  assert.ok(integration.includes("NOT has_table_privilege(current_user,"));
  assert.ok(integration.includes("LEFTERIA FC"));
});
