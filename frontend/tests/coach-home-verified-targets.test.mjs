import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const component=readFileSync(new URL('../src/lib/mobile/components/MobileCoachFeedbackTargets.svelte',import.meta.url),'utf8');
const controller=readFileSync(new URL('../src/lib/mobile/controller.ts',import.meta.url),'utf8');
const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
test('coach target panel is a strict private, current-assignment read',()=>{
  assert.match(component,/controller\.getCoachFeedbackTargets\(\)/);
  assert.match(component,/target\.organization_id==='lefteria_fc_academy'/);
  assert.match(component,/return\(\)=>\{alive=false;revision\+\+;targets=\[\];\}/);
  assert.match(controller,/state\.user\?\.role!=='coach'/);
  assert.match(controller,/validCoachFeedbackTargets\(result\)/);
  assert.doesNotMatch(component,/fetch\(|localStorage|mockPlayer|playerPhone|birthday|date_of_birth/);
  assert.ok(compile(component,{filename:'MobileCoachFeedbackTargets.svelte',generate:'server'}).js.code.length>0);
});
test('coach panel mounts on coach home only and opens actual guarded feedback editor',()=>{
  assert.match(route,/role==='coach'\}\s*<MobileCoachFeedbackTargets/);
  assert.match(route,/onOpenFeedback=\{\(\)=>openTab\('squad'\)\}/);
  assert.match(route,/activeTab === 'squad' && role === 'coach'/);
});
