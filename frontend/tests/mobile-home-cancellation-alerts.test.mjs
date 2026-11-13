import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {parentHomeCancelledEvents,parentHomeSchedule}
  from '../src/lib/mobile/parent-home.ts';
import {trustedRoleHomeCancelledEvents,trustedRoleHomeEvents}
  from '../src/lib/mobile/role-home.ts';

const parentUi=readFileSync(new URL('../src/lib/mobile/components/MobileParentHome.svelte',import.meta.url),'utf8');
const roleUi=readFileSync(new URL('../src/lib/mobile/components/MobileRoleHome.svelte',import.meta.url),'utf8');
const today='2026-11-13';
const identity='parent-verified';
const family={children:[
  {id:'kid-1',name:'Παιδί 1',organization_id:'lefteria_fc_academy'},
  {id:'kid-2',name:'Παιδί 2',organization_id:'lefteria_fc_academy'}
]};
const event=(id,extra={})=>({
  id,kind:'training',organization_id:'lefteria_fc_academy',
  date:'2026-11-18',end_date:'2026-11-18',title:'Προπόνηση',
  is_cancelled:true,cancel_reason:'Ακατάλληλο γήπεδο',start_time:'17:00',...extra
});
const parentCal=(extra={})=>({
  identity_id:identity,role:'parent',organization_id:'lefteria_fc_academy',
  selected_player_id:'kid-1',month:'2026-11',
  items:[event('cancel'),event('active',{is_cancelled:false}),
    event('past',{date:'2026-11-01',end_date:'2026-11-01'}),
    event('other',{organization_id:'lefteria_fc'})],...extra
});

test('cancelled parent events are child-scoped and never offered as active RSVP schedule',()=>{
  assert.deepEqual(parentHomeCancelledEvents(family,parentCal(),identity,today,'kid-1').map(x=>x.id),['cancel']);
  assert.deepEqual(parentHomeSchedule(family,parentCal(),identity,today,'kid-1').map(x=>x.id),['active']);
  for(const modified of [
    parentCal({identity_id:'intruder'}),
    parentCal({role:'coach'}),
    parentCal({selected_player_id:'kid-2'}),
    parentCal({organization_id:'lefteria_fc'}),
    parentCal({month:'2026-12'})
  ])assert.deepEqual(parentHomeCancelledEvents(family,modified,identity,today,'kid-1'),[]);
  assert.deepEqual(parentHomeCancelledEvents(family,parentCal(),identity,today,'kid-2'),[]);
  assert.deepEqual(parentHomeCancelledEvents({children:[]},parentCal(),identity,today),[]);
  assert.deepEqual(parentHomeCancelledEvents(family,null,identity,today),[]);
});

test('role-specific cancelled alerts inherit current identity and owner gate',()=>{
  const make=(role='player',items=[event('cancel'),event('active',{is_cancelled:false})])=>({
    identity_id:'member-1',role,organization_id:role==='coach'?null:'lefteria_fc_academy',
    selected_player_id:role==='player'?'self':null,
    players:role==='player'?[{id:'self',organization_id:'lefteria_fc_academy'}]:[],
    month:'2026-11',items
  });
  const player=make();
  assert.deepEqual(trustedRoleHomeCancelledEvents(player,'player','member-1',null,today).map(x=>x.id),['cancel']);
  assert.deepEqual(trustedRoleHomeEvents(player,'player','member-1',null,today).map(x=>x.id),['active']);
  assert.deepEqual(trustedRoleHomeCancelledEvents(player,'player','another',null,today),[]);
  assert.deepEqual(trustedRoleHomeCancelledEvents({...player,month:'2026-12'},'player','member-1',null,today),[]);
  assert.deepEqual(trustedRoleHomeCancelledEvents({...player,selected_player_id:'sibling'},'player','member-1',null,today),[]);
  const coach=make('coach',[event('cancel'),event('club',{organization_id:'lefteria_fc'})]);
  assert.deepEqual(trustedRoleHomeCancelledEvents(coach,'coach','member-1',null,today).map(x=>x.id),['cancel','club']);
  const manager=make('management');
  assert.deepEqual(trustedRoleHomeCancelledEvents(manager,'management','member-1','lefteria_fc_academy',today).map(x=>x.id),['cancel']);
  assert.deepEqual(trustedRoleHomeCancelledEvents(manager,'management','member-1','lefteria_fc',today),[]);
});

test('both role homes compile and display explicit cancellation panel with no RSVP action',()=>{
  assert.ok(compile(parentUi,{filename:'MobileParentHome.svelte',generate:'server'}).js.code.length>0);
  assert.ok(compile(roleUi,{filename:'MobileRoleHome.svelte',generate:'server'}).js.code.length>0);
  assert.match(parentUi,/data-testid="parent-home-cancellations"/);
  assert.match(parentUi,/activeChild\.id/);
  assert.match(parentUi,/parentHomeCancelledEvents/);
  assert.match(roleUi,/data-testid="role-home-cancellations"/);
  assert.match(roleUi,/trustedRoleHomeCancelledEvents/);
  assert.match(parentUi,/Οι παλιές δηλώσεις συμμετοχής διατηρούνται/);
  assert.match(roleUi,/Οι προηγούμενες απαντήσεις συμμετοχής διατηρούνται/);
  assert.doesNotMatch(parentUi,/localStorage|{@html}|innerHTML|mockCalendar/);
  assert.doesNotMatch(roleUi,/localStorage|{@html}|innerHTML|mockCalendar/);
});
