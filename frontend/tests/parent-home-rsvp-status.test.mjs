import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {parentHomeRsvp,matchingParentHomeCalendar} from '../src/lib/mobile/parent-home.ts';

const source=readFileSync(new URL('../src/lib/mobile/components/MobileParentHome.svelte',import.meta.url),'utf8');
const event=(responses)=>({id:'training',kind:'training',organization_id:'lefteria_fc_academy',
  responses,date:'2026-11-20',end_date:'2026-11-20',title:'Training',is_cancelled:false});
test('home never misattributes a sibling or unrelated child RSVP to the selected child',()=>{
  const e=event([{player_id:'a',status:'going'},{player_id:'b',status:'not_going'}]);
  assert.equal(parentHomeRsvp(e,'a'),'going');
  assert.equal(parentHomeRsvp(e,'b'),'not_going');
  assert.equal(parentHomeRsvp(e,'c'),null);
  assert.equal(parentHomeRsvp(e,null),null);
  assert.equal(parentHomeRsvp(event([{player_id:'a',status:'going'},{player_id:'a',status:'maybe'}]),'a'),null);
  assert.equal(parentHomeRsvp(event([{player_id:'a',status:'present'}]),'a'),null);
  assert.equal(parentHomeRsvp(event([]),'a'),null);
  assert.equal(parentHomeRsvp({...event([]),responses:undefined},'a'),null);
});
test('RSVP is displayed only inside identity-verified linked-child schedule',()=>{
  assert.match(source,/const trustworthyCalendar = \$derived/);
  assert.match(source,/matchingParentHomeCalendar\(family, calendar, identityId, today, activeChild\?\.id\)/);
  assert.match(source,/parentHomeRsvp\(event,activeChild\.id\)/);
  assert.match(source,/data-testid="parent-home-event-rsvp"/);
  assert.match(source,/Δήλωση συμμετοχής:/);
  assert.doesNotMatch(source,/automaticAttendance|updateAttendance|mockResponse|localStorage/);
  assert.ok(compile(source,{filename:'MobileParentHome.svelte',generate:'server'}).js.code.length>0);
});
