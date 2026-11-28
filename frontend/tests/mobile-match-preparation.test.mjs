import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {matchAvailability,matchRosterSummary,filterMatchRoster} from '../src/lib/mobile/match-preparation.ts';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const panel=read('frontend/src/lib/mobile/components/MobileMatchPreparation.svelte');
const calendar=read('frontend/src/lib/mobile/components/MobileCalendar.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const endpoints=read('backend/src/LefteriaFc.Api/ClubCalendar/MobileCalendarEndpoints.cs');
const player=(id,name,number,position,status)=>({id,name,number,position,status,image_url:null,updated_at:null});
const roster=[
  player('a','Αλέξανδρος',7,'Επιθετικός','going'),
  player('b','Μάριος',1,'Τερματοφύλακας','maybe'),
  player('c','Ανδρέας',4,'Αμυντικός','not_going'),
  player('d','Γιώργος',9,'Επιθετικός',null),
  player('e','Πέτρος',5,'Αμυντικός','no_response')
];
test('availability summary separates uncertainty and imported unanswered statuses from confirmed availability',()=>{
  assert.deepEqual(matchRosterSummary(roster),{counts:{going:1,maybe:1,not_going:1,unanswered:2},total:5});
  assert.equal(matchAvailability(null),'unanswered');
  assert.equal(matchAvailability('no_response'),'unanswered');
  assert.deepEqual(matchRosterSummary([]),{counts:{going:0,maybe:0,not_going:0,unanswered:0},total:0});
});
test('duplicate roster identities cannot inflate counts and latest verified row wins consistently',()=>{
  const updated=[...roster,{...roster[0],status:'maybe'}];
  assert.deepEqual(matchRosterSummary(updated),{counts:{going:0,maybe:2,not_going:1,unanswered:2},total:5});
  assert.deepEqual(filterMatchRoster(updated,'maybe','').map(p=>p.id),['a','b']);
  assert.deepEqual(filterMatchRoster(updated,'going',''),[]);
});
test('search combines status with name, position or exact shirt number',()=>{
  assert.deepEqual(filterMatchRoster(roster,'all','  ΜΆΡΙΟΣ ').map(p=>p.id),['b']);
  assert.deepEqual(filterMatchRoster(roster,'unanswered','αμυντικός').map(p=>p.id),['e']);
  assert.deepEqual(filterMatchRoster(roster,'all','7').map(p=>p.id),['a']);
  assert.deepEqual(filterMatchRoster(roster,'going','1'),[]);
});
test('match workboard remains read-only, scoped, and explicit when data is partial or invalid',()=>{
  compile(panel,{filename:'MobileMatchPreparation.svelte',generate:'client'});
  compile(calendar,{filename:'MobileCalendar.svelte',generate:'client'});
  assert.match(panel,/responses\.next_offset!==null\|\|loadingMore/);
  assert.match(panel,/Οι δηλώσεις συμμετοχής δεν αποτελούν επιλογή αποστολής/);
  assert.doesNotMatch(panel,/localStorage|sessionStorage|fetch\(|onRespond/);
  assert.match(calendar,/\{:else if staff\}/);
  assert.match(calendar,/responses\.event_id===item\.id && responses\.event_type===item\.kind && responses\.organization_id===item\.organization_id/);
  assert.match(calendar,/\{#if item\.kind==='match'\}/);
  assert.match(calendar,/\{#if responsesError\}[\s\S]*?\{:else\}[\s\S]*?<MobileMatchPreparation/);
  assert.match(controller,/data\.players\.length > 200/);
  assert.match(controller,/new Set\(data\.players\.map\(player => player\?\.id\)\)\.size !== data\.players\.length/);
  assert.match(controller,/calendarResponses: null, calendarResponsesStatus: 'error'/);
  assert.match(endpoints,/MapGet\("\/api\/mobile\/calendar\/\{event_type\}\/\{event_id\}\/responses"[\s\S]*?CacheControl="private, no-store"/);
});
