import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {matchingParentHomeCalendar,parentHomeSchedule} from '../src/lib/mobile/parent-home.ts';
import {compile} from 'svelte/compiler';
const source=readFileSync(new URL('../src/lib/mobile/components/MobileParentHome.svelte',import.meta.url),'utf8');
const family={children:[
  {id:'child-a',name:'Alpha',organization_id:'lefteria_fc_academy',team_type:'Academy'},
  {id:'child-b',name:'Beta',organization_id:'lefteria_fc',team_type:'First Team'}
]};
const event={id:'t1',kind:'training',event_type:'training',title:'Session B',
  organization_id:'lefteria_fc',date:'2026-11-20',end_date:'2026-11-20',
  is_cancelled:false,start_time:'16:00'};
const calendar={role:'parent',identity_id:'parent-1',organization_id:'lefteria_fc',
  selected_player_id:'child-b',month:'2026-11',items:[event]};
test('second child calendar is visible only with explicit current family membership',()=>{
  assert.equal(matchingParentHomeCalendar(family,calendar,'parent-1','2026-11-13','child-b'),true);
  assert.deepEqual(parentHomeSchedule(family,calendar,'parent-1','2026-11-13','child-b').map(x=>x.id),['t1']);
  assert.equal(matchingParentHomeCalendar(family,calendar,'parent-1','2026-11-13'),false);
  assert.equal(matchingParentHomeCalendar(family,calendar,'other','2026-11-13','child-b'),false);
  assert.equal(matchingParentHomeCalendar(family,calendar,'parent-1','2026-11-13','child-unknown'),false);
  assert.equal(matchingParentHomeCalendar({children:[family.children[0]]},calendar,'parent-1','2026-11-13','child-b'),false);
  assert.equal(matchingParentHomeCalendar(family,{...calendar,organization_id:'lefteria_fc_academy'},'parent-1','2026-11-13','child-b'),false);
});
test('parent home resets visibility on sibling selection, role removal and rejected authority',()=>{
  assert.match(source,/selectedChildId = \$state<string \| null>\(null\)/);
  assert.match(source,/family\?\.children\.find\(child=>child\.id===selectedChildId\)/);
  assert.match(source,/aria-pressed=\{activeChild\?\.id===child\.id\}/);
  assert.match(source,/onLoadCalendar\(parentHomeCalendarMonth\(today\), activeChild\.id\)/);
  assert.match(source,/matchingParentHomeCalendar\(family, calendar, identityId, today, activeChild\?\.id\)/);
  assert.match(source,/parentHomeSchedule\(family, calendar, identityId, today, activeChild\?\.id\)/);
  assert.ok(compile(source,{filename:'MobileParentHome.svelte',generate:'server'}).js.code.length>0);
  assert.doesNotMatch(source,/localStorage|sessionStorage|date_of_birth|medical_notes/);
});
