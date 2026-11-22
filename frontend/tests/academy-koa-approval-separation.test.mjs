import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../'+path,import.meta.url),'utf8');
const page=read('src/routes/academy/registration/+page.svelte');
const api=read('../backend/src/LefteriaFc.Api/Marketing/AcademyRegistrationEndpoints.cs');
const template=read('../docs/implementation/academy-koa-parent-viber-template-2026-11-22.md');

test('KOA status is per child; parent can opt into Viber but instructions are NOT on the form',()=>{
  assert.match(page,/koa_health_card_status/);
  assert.match(page,/data-testid="koa-card-yes"/);
  assert.match(page,/data-testid="koa-card-no"/);
  assert.match(page,/bind:group=\{child\.koa_health_card_status\}/);
  assert.match(page,/data-testid="consent-viber"/);
  assert.doesNotMatch(page,/14 εργάσιμες|Μέλαθρον|ACS|€5 μετρητά/);
  assert.doesNotThrow(()=>compile(page,{filename:'academy/registration/+page.svelte',generate:'server'}));
});
test('private KOA state and Viber preference are sealed, never in public projection',()=>{
  assert.match(api,/ValidKoa\(child\)/);
  assert.match(api,/child\.KoaHealthCardStatus,body\.ConsentViber/);
  const p=api.slice(api.indexOf('var projection=JsonSerializer.Serialize(new'),api.indexOf('await using var command=new NpgsqlCommand'));
  assert.doesNotMatch(p,/KoaHealthCardStatus|ConsentViber/);
  assert.doesNotMatch(api,/SendViber|ViberClient|SendAsync\(/);
});
test('post-placement template orders welcome first and KOA only if absent',()=>{
  assert.match(template,/welcome-to-team/);
  assert.match(template,/card is recorded as \*\*present\*\*, do not queue KOA/);
  assert.match(template,/14 εργάσιμες ημέρες/);
  assert.match(template,/€5 μετρητά μέσα στον φάκελο/);
  assert.match(template,/Η ACS χρεώνει ξεχωριστά/);
  assert.match(template,/https:\/\/cso\.org\.cy\/health-journal\//);
});
