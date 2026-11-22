import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {
  registrationNeedsAttention,registrationNextStep,
  filterRegistrations,registrationPageSummary
} from '../src/lib/admin/academy-registration-triage.ts';

const example=(status,i=1,player=null)=>({
  id:'registration-'+i,status,applicant_first_name:'Παιδί',
  applicant_last_name:'Λευτεριά',guardian_name:'Ελένη '+i,
  onboarded_player_id:player
});
const rows=[
  example('pending',1),example('under_review',2),example('needs_information',3),
  example('approved',4),example('approved',5,'verified-player'),
  example('rejected',6),example('withdrawn',7)
];

test('registration queue follows real status and enrollment, not guessed guardian access',()=>{
  assert.deepEqual(filterRegistrations(rows,'action','').map(r=>r.id),
    ['registration-1','registration-2','registration-3','registration-4']);
  assert.deepEqual(filterRegistrations(rows,'approved','').map(r=>r.id),
    ['registration-4','registration-5']);
  assert.deepEqual(filterRegistrations(rows,'closed','').map(r=>r.id),
    ['registration-6','registration-7']);
  assert.equal(registrationNeedsAttention(rows[4]),false);
  assert.match(registrationNextStep(rows[4]),/ξεχωριστά/);
  assert.match(registrationNextStep(rows[3]),/δεν έχει συνδεθεί/);
  assert.deepEqual(registrationPageSummary(rows),{
    loaded:7,needsAttention:4,linked:1,awaitingGuardianInfo:1
  });
});

test('search is scoped to supplied authorized page and does not invent totals',()=>{
  assert.equal(filterRegistrations(rows,'all','Ελένη 2')[0]?.id,'registration-2');
  assert.equal(filterRegistrations(rows,'all','registration-3')[0]?.id,'registration-3');
  assert.deepEqual(filterRegistrations(rows.slice(2,3),'action','Ελένη 2'),[]);
  assert.equal(registrationPageSummary(rows.slice(0,2)).loaded,2);
  assert.deepEqual(filterRegistrations([], 'action',''),[]);
  assert.equal(registrationNextStep(example('unknown')), 'Επαληθεύστε την τρέχουσα κατάσταση πριν από αλλαγές.');
});

test('Academy Office registration triage compiles and preserves parent proof boundary',()=>{
  const page=readFileSync(new URL('../src/routes/admin/academy-office/+page.svelte',import.meta.url),'utf8');
  assert.ok(compile(page,{filename:'admin/academy-office/+page.svelte',generate:'server'}).js.code.length>100);
  for(const marker of ['academy-registration-triage','academy-registration-page-summary',
    'academy-inbox-status','academy-inbox-search','academy-registration-next-step',
    'academy-inbox-no-filter-results','academy-registration-triage-card',
    'AcademyGuardianProofReview','onboarded_player_id']) assert.ok(page.includes(marker),marker);
  assert.match(page,/page\.url\.searchParams\.get\('queue'\)==='action'/);
  assert.match(page,/filterRegistrations\(entries,applicationFilter,applicationQuery\)/);
  assert.match(page,/registrationPageSummary\(entries\)/);
  assert.match(page,/\$auth\.session\.user\.role === 'super_admin'/);
});
