import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const home=readFileSync(new URL('../src/lib/mobile/components/MobileParentHome.svelte',import.meta.url),'utf8');
const app=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
test('parent home quick actions route to implemented private chat and training fees',()=>{
  assert.match(home,/onMessages\?: \(\) => void \| Promise<void>/);
  assert.match(home,/onFinancials\?: \(\) => void \| Promise<void>/);
  assert.match(home,/data-testid="parent-shortcut-messages"/);
  assert.match(home,/data-testid="parent-shortcut-financials"/);
  assert.match(home,/onclick=\{\(\)=>void onMessages\(\)\}/);
  assert.match(home,/onclick=\{\(\)=>void onFinancials\(\)\}/);
  assert.match(app,/onMessages=\{\(\) => openTab\('chat'\)\}/);
  assert.match(app,/onFinancials=\{\(\) => openTab\('financials'\)\}/);
  assert.match(app,/activeTab === 'financials' && role === 'parent'/);
  assert.match(app,/<MobileTrainingFees \{controller\}\/>/);
  assert.match(app,/activeTab === 'chat' && role !== 'fan'/);
  assert.ok(compile(home,{filename:'MobileParentHome.svelte',generate:'server'}).js.code.length>0);
});
test('shortcuts do not invent transactions or bypass current session role checks',()=>{
  assert.doesNotMatch(home,/fetch\(|localStorage|paymentToken|cardNumber|chargeSuccessful/);
  assert.match(app,/!user\.current_role_available/);
});
