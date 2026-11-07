import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/Trials/FirstTeamTrialPublicEndpoints.cs');
const bootstrap=read('backend/src/LefteriaFc.Api/Program.cs');
const checks=read('backend/tests/LefteriaFc.Identity.IntegrationTests/IdentityHttpChecks.TrialSettings.cs');

test('original first-team trials setting is a real public GET with closed-only defaults',()=>{
  assert.ok(api.includes('MapGet("/api/trials/settings"'));
  assert.ok(api.includes('FirstTeamTrialsSettingsPolicy.Default'));
  assert.ok(api.includes('CacheControl = "no-store"'));
  assert.ok(api.includes('WithName("GetFirstTeamTrialSettings")'));
  assert.ok(bootstrap.includes('app.MapFirstTeamTrialPublic();'));
  assert.doesNotMatch(api,/MapPost\(|MapPut\(|MapDelete\(|HttpClient|INSERT INTO|ExecuteSqlRaw/);
});
test('runtime contract forbids unreviewed registration and private data exposure',()=>{
  assert.ok(checks.includes('FirstTeamTrialSettingsClosedAsync'));
  assert.ok(checks.includes('!json.GetProperty("open").GetBoolean()'));
  assert.ok(checks.includes('json.EnumerateObject().Count()==5'));
  assert.ok(checks.includes('"/api/trials/submit"'));
  assert.ok(checks.includes('HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed'));
});
