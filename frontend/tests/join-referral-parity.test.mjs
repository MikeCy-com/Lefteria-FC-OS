import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {partnerReferralSlug} from '../src/lib/public/referral.ts';

const url=(query)=>new URL('https://lefteria.invalid/join'+query);
const source=(path)=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');

test('source join referral accepts only one bounded safe lowercase slug',()=>{
  for(const slug of ['ts-international-soccer-academy','academy','a','t2-3','a'.repeat(64)]){
    assert.equal(partnerReferralSlug(url('?partner='+slug)),slug);
  }
  for(const query of ['', '?partner=', '?partner=A', '?partner=../admin',
    '?partner=%3Cscript%3E', '?partner=%20abc', '?partner=a--', '?partner=-abc',
    '?partner=abc&partner=other','?partner='+('a'.repeat(65))]){
    assert.equal(partnerReferralSlug(url(query)),null,query);
  }
  assert.equal(partnerReferralSlug(url('?partner=academy&organization_id=lefteria_fc')),'academy');
});

test('original Academy join has source referral badge but never fakes a lead write',()=>{
  const page=source('routes/join/+page.svelte');
  const loader=source('routes/join/+page.server.ts');
  const journey=source('lib/public/PublicJourney.svelte');
  assert.match(page,/partnerReferral=\{data\.partnerReferral \?\? null\}/);
  assert.match(page,/organization="lefteria_fc_academy"/);
  assert.match(loader,/partnerReferralSlug\(url\)/);
  assert.match(loader,/loadPublicSite\(url,'lefteria_fc_academy'\)/);
  assert.match(journey,/data-testid="partner-referral-badge"/);
  assert.match(journey,/\{partnerReferral\}/);
  assert.doesNotMatch(page,/<form|submit|axios|fetch\(/i);
  assert.doesNotMatch(loader,/POST|insert|write|sms|twilio/i);
});
