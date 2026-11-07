import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const reader=source('backend/src/LefteriaFc.Infrastructure/Trials/PostgresFirstTeamTrialDisplayReader.cs');
const endpoint=source('backend/src/LefteriaFc.Api/Trials/FirstTeamTrialPublicEndpoints.cs');
const di=source('backend/src/LefteriaFc.Infrastructure/InfrastructureServiceCollectionExtensions.cs');

test('first-team display adapter can call only source-scoped stored projection',()=>{
  assert.ok(reader.includes('FROM club.read_first_team_trial_display()'));
  assert.ok(reader.includes('Open: false'));
  assert.ok(reader.includes('FirstTeamTrialsSettingsPolicy.TryValidate(candidate'));
  assert.ok(reader.includes('catch (NpgsqlException) { return null; }'));
  assert.doesNotMatch(reader,/^\s*(?:INSERT INTO|UPDATE\s+club\.|DELETE FROM|GRANT|REVOKE)\b/im);
  assert.ok(di.includes('AddScoped<IFirstTeamTrialDisplayReader, PostgresFirstTeamTrialDisplayReader>()'));
});

test('first-team public GET uses verified closed reader but never a submission write',()=>{
  assert.ok(endpoint.includes('IFirstTeamTrialDisplayReader reader'));
  assert.ok(endpoint.includes('reader.ReadAsync(token)'));
  assert.ok(endpoint.includes('settings is { Open: false }'));
  assert.ok(endpoint.includes('FirstTeamTrialsSettingsPolicy.Default'));
  assert.ok(endpoint.includes('CacheControl = "no-store"'));
  assert.doesNotMatch(endpoint,/MapPost\(|MapPut\(|MapDelete\(|applicant|medical|athlete/i);
});
