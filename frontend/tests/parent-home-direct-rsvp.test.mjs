import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const home=readFileSync(new URL('../src/lib/mobile/components/MobileParentHome.svelte',import.meta.url),'utf8');
const app=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
const controller=readFileSync(new URL('../src/lib/mobile/controller.ts',import.meta.url),'utf8');
test('RSVP controls exist only inside verified selected child events and submit actual status',()=>{
  assert.match(home,/const trustworthyCalendar = \$derived/);
  assert.match(home,/\{#each upcoming as event/);
  assert.match(home,/\{#if onRespond && !event\.is_cancelled\}/);
  assert.match(home,/data-testid="parent-home-rsvp-actions"/);
  assert.match(home,/aria-pressed=\{ownRsvp===choice\[0\]\}/);
  assert.match(home,/onclick=\{\(\)=>void onRespond\?\.\(event,choice\[0\] as AvailabilityStatus\)\}/);
  assert.match(home,/disabled=\{busy\}/);
  assert.match(app,/onRespond=\{\(item,status\)=>controller\.submitAvailability\(item,status\)\}/);
  assert.ok(compile(home,{filename:'MobileParentHome.svelte',generate:'server'}).js.code.length>0);
});
test('existing backend controller refuses unverified players and refreshes confirmed availability',()=>{
  assert.match(controller,/identity\.role !== 'parent' && identity\.role !== 'player'/);
  assert.match(controller,/!calendar\?\.selected_player_id \|\| !currentItem \|\| currentItem\.is_cancelled/);
  assert.match(controller,/api\.POST\('\/api\/mobile\/availability'/);
  assert.match(controller,/if \(!await loadCalendar\(calendar\.month, playerId\)/);
  assert.doesNotMatch(home,/fetch\(|localStorage|mockRsvp|optimisticRsvp/);
});
