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
  assert.match(api,/receipt=receipts\[0\],receipts/);
  assert.doesNotMatch(api,/console\.log|logger\.LogInformation\(.*[Bb]ody/);
});

test('family registration preserves per-child medical details and mandatory printing',()=>{
  assert.match(page,/data-testid="add-player"/);
  assert.match(page,/data-testid="additional-player"/);
  assert.match(page,/data-testid="additional-medical"/);
  assert.match(page,/preferred_number/);
  assert.match(page,/preferred_print_name/);
  assert.match(page,/υποχρεωτική εκτύπωση/);
  assert.ok(page.includes('playerCount===1?60:100+(playerCount-2)*50'));
  assert.match(page,/additional_players:additionalPlayers/);
  assert.match(api,/PrintingFeeEur=10/);
  assert.match(api,/KitFeeEur=140/);
  assert.match(api,/OfficialKitMandatory=true,PrintingMandatory=true/);
  assert.match(api,/BeginTransactionAsync/);
  assert.match(api,/CommitAsync/);
  assert.doesNotMatch(api,/KitPrintingRequested|OfficialKitRequested/);
});

test('same-origin SvelteKit proxy strips credentials and bounds upload payload',()=>{
  assert.match(proxy,/request\.headers\.get\('origin'\) !== url\.origin/);
  assert.match(proxy,/size > 131072/);
  assert.match(proxy,/credentials: 'omit'/);
  assert.match(proxy,/cache: 'no-store'/);
  assert.match(proxy,/response\.status === 202/);
  assert.match(proxy,/new Set\(result\.receipts\)\.size===result\.receipts\.length/);
  assert.match(proxy,/status: 503/);
  assert.doesNotMatch(proxy,/request\.headers\.get\('cookie'\)|request\.headers\.get\('authorization'\)/i);
});

test('one CRM list covers public and manual registrations with SQL-enforced row audiences',()=>{
  const endpoint=load('../../backend/src/LefteriaFc.Api/Marketing/AcademyIntakeAdminEndpoints.cs');
  const office=load('../src/routes/admin/academy-office/+page.svelte');
  const migration=load('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115140000_AcademyPublicRegistrationSubmission.cs');
  const unified=load('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115910000_CrmUnifiedAcademyRegistrationInbox.cs');
  const catalog=load('../src/lib/admin/module-catalog.ts');
  assert.match(program,/app\.MapAcademyIntakeAdmin\(\)/);
  assert.match(endpoint,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(endpoint,/session\.User\.Role!="super_admin"/);
  assert.match(endpoint,/read_admin_registration_queue/);
  assert.match(migration,/identity\.lock_admin_club_authority/);
  assert.match(migration,/v_role<>'super_admin'/);
  assert.match(migration,/REVOKE ALL ON FUNCTION academy_intake\.read_admin_registration_queue/);
  assert.match(office,/data-testid="academy-registration-unified-inbox"/);
  assert.match(office,/api \+ collection/);
  assert.doesNotMatch(office,/fetch\('\/api\/admin\/academy\/intake/);
  assert.doesNotMatch(office,/data-testid="academy-public-intake"/);
  assert.match(unified,/actor_role='super_admin' OR recorded_by=actor/g);
  assert.match(unified,/AS submission_source/);
  assert.match(unified,/CREATE OR REPLACE FUNCTION identity\.admin_academy_office/);
  assert.match(catalog,/name: 'Εγγραφές Ακαδημίας'/);
  assert.doesNotMatch(endpoint,/evidence_ciphertext|signature_data|allergies_details/);
  assert.doesNotMatch(migration,/jsonb_build_object\([^)]*evidence_ciphertext/);
});
