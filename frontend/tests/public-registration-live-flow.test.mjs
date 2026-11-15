import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const load = (path) => readFileSync(new URL(path,import.meta.url),'utf8');
const page=load('../src/routes/academy/registration/+page.svelte');
const proxy=load('../src/routes/api/public/academy/registration/+server.ts');
const api=load('../../backend/src/LefteriaFc.Api/Marketing/AcademyRegistrationEndpoints.cs');
const migration=load('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115140000_AcademyPublicRegistrationSubmission.cs');
const program=load('../../backend/src/LefteriaFc.Api/Program.cs');

test('Svelte academy registration compiles and has all original five functional steps',()=>{
  assert.doesNotThrow(()=>compile(page,{filename:'academy/registration/+page.svelte',generate:'server'}));
  for(let index=1;index<=5;index++) assert.match(page,new RegExp('data-testid="step-'+index+'"'));
  for(const id of ['player-first-name','player-last-name','player-dob','player-gender',
    'parent-name','parent-phone','parent-email','emergency-name','emergency-phone',
    'allergies-details','conditions-details','medication-details',
    'consent-participation','consent-medical','consent-gdpr','media-yes','media-no',
    'consent-liability','consent-financial','payment-cash','payment-card',
    'payment-transfer','signature-canvas','signature-date','next-step-btn',
    'prev-step-btn','submit-btn','registration-submit-error','registration-success'])
    assert.ok(page.includes('data-testid="'+id+'"'),id);
  assert.doesNotMatch(page,/<fieldset disabled(?:\s|>)/);
  assert.match(page,/if\(response\.status===202 && typeof result\?\.receipt/);
  assert.doesNotMatch(page,/https:\/\/lefteriafc\.cy\/academy\/registration/);
});

test('public intake only creates pending Academy records with encrypted evidence and separate consent decisions',()=>{
  assert.match(program,/app\.MapAcademyRegistration\(\)/);
  assert.match(migration,/VALUES\(_id,'lefteria_fc_academy'/);
  assert.match(migration,/INSERT INTO academy_intake\.registration_sealed_evidence/);
  assert.match(migration,/INSERT INTO academy_intake\.registration_consents/);
  assert.match(migration,/registration_rate_buckets/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/REVOKE ALL ON FUNCTION academy_intake\.submit_public_registration/);
  assert.doesNotMatch(migration,/GRANT EXECUTE|GRANT (?:INSERT|UPDATE|SELECT)/);
  assert.doesNotMatch(migration,/INSERT INTO club\.players|INSERT INTO finance\./);
  assert.match(api,/new AesGcm\(key,tag\.Length\)/);
  assert.match(api,/CryptographicOperations\.ZeroMemory/);
  assert.match(api,/AcademyRegistration:Enabled/);
  assert.match(api,/AcademyRegistration:PolicyVersion/);
  assert.match(api,/AcademyEnquiryActivationGate\.TryCreateFingerprint/);
  assert.match(api,/Duplicate|duplicate_field/);
  assert.match(api,/Results\.Json\(new \{receipt=id\.ToString\("N"\)\},statusCode:202\)/);
  assert.doesNotMatch(api,/console\.log|logger\.LogInformation\(.*[Bb]ody/);
});

test('same-origin SvelteKit proxy strips credentials and bounds upload payload',()=>{
  assert.match(proxy,/request\.headers\.get\('origin'\) !== url\.origin/);
  assert.match(proxy,/size > 32768/);
  assert.match(proxy,/credentials: 'omit'/);
  assert.match(proxy,/cache: 'no-store'/);
  assert.match(proxy,/response\.status === 202/);
  assert.match(proxy,/status: 503/);
  assert.doesNotMatch(proxy,/request\.headers\.get\('cookie'\)|request\.headers\.get\('authorization'\)/i);
});
