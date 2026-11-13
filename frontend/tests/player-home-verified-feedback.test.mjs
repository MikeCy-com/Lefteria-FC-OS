import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const app=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
const feedback=readFileSync(new URL('../src/lib/mobile/components/MobilePlayerCoachFeedback.svelte',import.meta.url),'utf8');
test('home displays verified coach feedback only for the currently authenticated player role',()=>{
  assert.match(app,/activeTab === 'home' && role !== 'parent' && !composing/);
  assert.match(app,/\{#if role==='player'\}\s*<MobilePlayerCoachFeedback \{controller\}\/>/);
  assert.match(app,/\{#key user\.id\+':'\+role/);
  assert.match(app,/!user\.current_role_available/);
  assert.match(feedback,/controller\.getPlayerCoachFeedback\(0\)/);
  assert.match(feedback,/item\.coach_name/);
  assert.match(feedback,/item\.message/);
  assert.match(feedback,/return\(\)=>\{alive=false;request\+\+;items=\[\];\}/);
  assert.ok(compile(feedback,{filename:'MobilePlayerCoachFeedback.svelte',generate:'server'}).js.code.length>0);
});
test('player home cannot manufacture or auto-publish coach text',()=>{
  assert.doesNotMatch(feedback,/mockFeedback|fakeCoach|localStorage|automaticPublish|autoSend|{@html}/);
});
