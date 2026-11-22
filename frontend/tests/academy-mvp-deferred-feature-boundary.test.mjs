import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const boundary=read('backend/src/LefteriaFc.Api/Release/AcademyMvpBoundary.cs');
const fees=read('backend/src/LefteriaFc.Api/Payabl/AcademyFeeEndpoints.cs');
const settings=read('.env.example');
const preflight=read('ops/release/staging-mvp-preflight.mjs');
const release=read('ops/release/go-live-gates.json');

test('Academy MVP mode is explicit, validates exact configuration and blocks before feature handlers',()=>{
  assert.match(program,/builder\.Configuration\["LEFTERIA_ACADEMY_MVP_ONLY"\]/);
  assert.match(program,/if \(!AcademyMvpBoundary\.ValidMode\(academyMvpMode\)\)/);
  assert.match(program,/if \(AcademyMvpBoundary\.Disabled\(academyMvpMode\)\)/);
  assert.match(program,/AcademyMvpBoundary\.IsOptionalEndpoint\(context\.Request\.Path\)/);
  assert.match(program,/code = "mvp_feature_disabled"/);
  assert.match(program,/if \(!AcademyMvpBoundary\.Disabled\(academyMvpMode\)\)\s+builder\.Services\.AddHostedService<HubFanLessonDeliveryWorker>\(\)/);
  assert.match(program,/StatusCodes\.Status503ServiceUnavailable/);
  assert.match(program,/CacheControl = "private, no-store"/);
  assert.ok(program.indexOf('AcademyMvpBoundary.IsOptionalEndpoint')<
    program.indexOf('app.UseMobileRequestGuards()'),
    'MVP protection must reject optional actions before request handlers.');
  assert.match(settings,/LEFTERIA_ACADEMY_MVP_ONLY=false/);
  assert.match(boundary,/setting is null or "true" or "false"/);
  assert.match(boundary,/setting == "true"/);
});

test('optional payment/POS/fan routes are denied but core finance, roster and guardian routes are preserved',()=>{
  for(const segment of [
    '/api/admin/pos','/api/admin/finance/payabl/payment-requests',
    '/api/payabl','/api/mobile/hub/fan-subscription',
    '/api/mobile/hub/fan-learning','/api/admin/finance/payabl/training-fees'
  ])assert.ok(boundary.includes(segment),'Missing opt-in MVP segment '+segment);
  assert.match(boundary,/segments\.Length == 2/);
  assert.match(boundary,/segments\[1\]\.Equals\("sms"/);
  assert.match(fees,/AcademyMvpBoundary\.Disabled\(configuration\["LEFTERIA_ACADEMY_MVP_ONLY"\]\)/);
  assert.match(fees,/body\.SaveCardRequested \|\| body\.AutomaticCollectionRequested/);
  assert.match(fees,/statusCode:StatusCodes\.Status503ServiceUnavailable/);
  assert.match(fees,/finance\.set_parent_fee_preferences/);
  assert.doesNotMatch(boundary, /StartsWithSegments\("\/api\/mobile\/finance"/);
  assert.doesNotMatch(boundary, /StartsWithSegments\("\/api\/admin\/football"/);
  assert.doesNotMatch(boundary, /StartsWithSegments\("\/api\/mobile\/family"/);
});

test('isolated staging must verify optional feature is disabled, not merely anonymously denied',()=>{
  for(const path of ['/api/admin/pos/sites','/api/admin/finance/payabl/payment-requests',
    '/api/mobile/hub/fan-subscription/plans'])
    assert.ok(preflight.includes(path),'Missing staging probe '+path);
  assert.match(preflight,/kind:'disabled',bodyCode:'mvp_feature_disabled'/);
  assert.match(preflight,/body\?\.code===check\.bodyCode/);
  assert.match(preflight,/kind:'deny'/);
  assert.match(release,/"id": "mvp-deferred-features"/);
  assert.match(release,/"status": "blocked"/);
  assert.doesNotMatch(program,/Payabl__Enabled=true|LEFTERIA_ACADEMY_MVP_ONLY=true/);
});
