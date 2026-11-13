import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const home=readFileSync(new URL('../src/lib/mobile/components/MobileRoleHome.svelte',import.meta.url),'utf8');
const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
const controller=readFileSync(new URL('../src/lib/mobile/controller.ts',import.meta.url),'utf8');
test('only actual player role can respond from verified next-match card',()=>{
  assert.match(home,/role==='player' && onRespond && !nextMatch\.is_cancelled/);
  assert.match(home,/data-testid="player-matchday-rsvp"/);
  assert.match(home,/playerRsvp===choice\[0\]/);
  assert.match(home,/nextMatch\.responses\.find\(r=>r\.player_id===calendar\?\.selected_player_id\)/);
  assert.match(home,/onRespond\?\.\(nextMatch,choice\[0\] as AvailabilityStatus\)/);
  assert.match(route,/onRespond=\{role==='player'\?\(item,status\)=>controller\.submitAvailability\(item,status\):undefined\}/);
  assert.match(home,/isTrustedRoleHomeContext\(calendar,role,identityId,organizationId,today\)/);
  assert.ok(compile(home,{filename:'MobileRoleHome.svelte',generate:'server'}).js.code.length>0);
});
test('controller reauthorizes current own player and verifies response before refresh',()=>{
  assert.match(controller,/identity\.role !== 'parent' && identity\.role !== 'player'/);
  assert.match(controller,/!calendar\?\.selected_player_id \|\| !currentItem \|\| currentItem\.is_cancelled/);
  assert.match(controller,/api\.POST\('\/api\/mobile\/availability'/);
  assert.match(controller,/data\.player_id !== playerId/);
  assert.match(controller,/await loadCalendar\(calendar\.month, playerId\)/);
  assert.doesNotMatch(home,/fakeRSVP|mockAvailability|localStorage|fetch\(/);
});
