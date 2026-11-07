import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(new URL(
  '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261107231500_FirstTeamTrialsSettings.cs',
  import.meta.url), 'utf8');
const permissions = readFileSync(new URL(
  '../../backend/tests/LefteriaFc.Identity.IntegrationTests/TestDatabase.cs',
  import.meta.url), 'utf8');

test('first-team trials settings belong only to FC and default closed', () => {
  assert.match(source, /CREATE TABLE club\.first_team_trial_settings/);
  assert.match(source, /REFERENCES club\.organizations\(id\) ON DELETE RESTRICT/);
  assert.match(source, /CHECK \(organization_id = 'lefteria_fc'\)/);
  assert.match(source, /is_open boolean NOT NULL DEFAULT false/);
  assert.match(source, /Οι εγγραφές για δοκιμαστικά είναι προσωρινά κλειστές/);
  assert.match(source, /Ετοιμος να Ξεκινησεις το Ταξιδι σου/);
  assert.match(source, /REVOKE ALL ON TABLE club\.first_team_trial_settings FROM PUBLIC/);
  assert.match(source, /ALTER TABLE club\.first_team_trial_settings ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(source, /^\s*(?:GRANT|INSERT INTO|CREATE POLICY)\b/im);
});
test('runtime cannot read or update private first-team trial settings', () => {
  assert.ok(permissions.includes("'club.first_team_trial_settings', 'SELECT'"));
  assert.ok(permissions.includes("'club.first_team_trial_settings', 'UPDATE'"));
  assert.ok(permissions.includes('column < 14'));
});
