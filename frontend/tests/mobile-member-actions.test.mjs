import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {stripTypeScriptTypes} from 'node:module';
import {calendarActions,feeActions} from '../src/lib/mobile/member-actions.ts';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const component=read('lib/mobile/components/MobileMemberActions.svelte');
const route=read('routes/app/[...path]/+page.svelte');
const controller=read('lib/mobile/controller.ts');
const today='2026-11-25';
const owner='lefteria_fc_academy';
const item=(id,extra={})=>({id,kind:'training',organization_id:owner,date:today,end_date:today,
  start_time:'17:00',is_cancelled:false,responses:[],...extra});
const calendar=(items,extra={})=>({role:'parent',organization_id:owner,selected_player_id:'child-a',
  items,next_offset:null,...extra});

test('calendar actions distinguish cancellation, no RSVP, answered, expired and unrelated events',()=>{
  const result=calendarActions(calendar([
    item('cancelled',{is_cancelled:true}),
    item('unanswered',{kind:'match'}),
    item('answered',{responses:[{player_id:'child-a',status:'maybe'}]}),
    item('other-owner',{organization_id:'lefteria_fc'}),
    item('past',{date:'2026-11-24',end_date:'2026-11-24'}),
    item('other-kind',{kind:'event'}),
    item('next-month',{date:'2026-12-01',end_date:'2026-12-01'}),
  ]),today);
  assert.deepEqual(result.cancelled.map(x=>x.id),['cancelled']);
  assert.deepEqual(result.unanswered.map(x=>x.id),['unanswered']);
  assert.equal(result.limited,false);
});
test('staff and unlinked identities never receive player RSVP prompts',()=>{
  const items=[item('match',{kind:'match'}),item('cancel',{is_cancelled:true})];
  for(const role of ['coach','management']){
    const result=calendarActions(calendar(items,{role,selected_player_id:null,
      organization_id:role==='coach'?null:owner}),today);
    assert.deepEqual(result.unanswered,[]);
    assert.equal(result.cancelled.length,1);
  }
  assert.deepEqual(calendarActions(calendar(items,{selected_player_id:null}),today).unanswered,[]);
});
test('action summaries are bounded and retain partial coverage indicator',()=>{
  const result=calendarActions(calendar(Array.from({length:5},(_,i)=>item('event-'+i))),today);
  assert.equal(result.unanswered.length,3);assert.equal(result.limited,true);
  assert.equal(calendarActions(calendar([],{next_offset:50}),today).limited,true);
});
test('issued, settled, review and paid fees cannot be conflated',()=>{
  const fee=(id,status,payment_status)=>({id,status,payment_status});
  assert.deepEqual(feeActions({fees:[
    fee('1','issued','ready'),fee('2','issued','not_created'),
    fee('3','issued','awaiting_settlement'),fee('4','issued','initiating'),
    fee('5','issued','paid'),fee('6','review_required','review_required'),
    fee('7','issued','review_required'),fee('8','paid','paid')
  ]}),{outstanding:2,review:2,settling:3});
  assert.throws(()=>feeActions({fees:[fee('1','issued','ready'),fee('1','issued','ready')]}),/Duplicate/);
});
test('action panel compiles and is isolated from the calendar workspace and unread counter',()=>{
  compile(component,{filename:'MobileMemberActions.svelte',generate:'client'});
  compile(component,{filename:'MobileMemberActions.svelte',generate:'server'});
  compile(route,{filename:'+page.svelte',generate:'client'});
  const start=controller.indexOf('async getMemberActionCalendar(');
  const end=controller.indexOf('async getAppNotificationInbox(',start);
  assert.ok(start>=0&&end>start);
  const method=controller.slice(start,end);
  assert.match(method,/calendarIdentity\(\)/);
  assert.match(method,/calendarScopeCurrent\(identity,bearer,owner\)/);
  assert.match(method,/validCalendar\(raw as MobileCalendar,identity,month,playerId,0\)/);
  assert.doesNotMatch(method,/update\(|clearCalendar\(|loadCalendar\(/);
  assert.match(component,/if\(role!=='parent'\)return/);
  assert.match(component,/validParentTrainingFees\(raw\)/);
  assert.match(component,/if\(!alive\|\|marker!==request\)return/);
  assert.match(component,/if\(!alive\|\|marker!==feeRequest\)return/);
  assert.match(component,/request\+\+;feeRequest\+\+;calendar=null;fees=null/);
  assert.doesNotMatch(component,/onUnreadChange|checkout_url|submitAvailability|localStorage|sessionStorage/);
  assert.match(route,/MobileMemberActions \{controller\} \{role\}/);
  assert.match(route,/onCalendar=\{childId=>openTab\('calendar',childId\)\}/);
});

function actionHarness(role,controller){
  const start=component.indexOf('  async function loadCalendar(');
  const end=component.indexOf('  onMount(',start);
  assert.ok(start>=0&&end>start);
  const handlers=stripTypeScriptTypes(component.slice(start,end));
  return new Function('controller','role','clubToday','validParentTrainingFees','feeActions',`
    let calendar=null,fees=null,calendarStatus='loading',feeStatus='loading';
    let today=clubToday(),selectedChild,request=0,feeRequest=0,alive=true;
    ${handlers}
    return {loadCalendar,loadFees,refresh,
      snapshot:()=>({calendar,fees,calendarStatus,feeStatus,selectedChild}),
      dispose:()=>{alive=false;request++;feeRequest++;}};
  `)(controller,role,()=>today,raw=>Array.isArray(raw?.fees),feeActions);
}
function deferred(){
  let resolve,reject;
  const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});
  return {promise,resolve,reject};
}
test('child switch discards earlier private response and failure clears the previous snapshot',async()=>{
  const old=deferred(),latest=deferred();let calls=0;
  const h=actionHarness('parent',{getMemberActionCalendar:()=>++calls===1?old.promise:
    calls===2?latest.promise:Promise.reject(new Error('Denied'))});
  const a=h.loadCalendar('child-a'),b=h.loadCalendar('child-b');
  latest.resolve(calendar([],{selected_player_id:'child-b'}));await b;
  old.resolve(calendar([item('private-child-a')],{selected_player_id:'child-a'}));await a;
  assert.equal(h.snapshot().selectedChild,'child-b');
  assert.deepEqual(h.snapshot().calendar.items,[]);
  await h.loadCalendar('child-b');
  assert.equal(h.snapshot().calendar,null);assert.equal(h.snapshot().calendarStatus,'error');
});
test('finance calls are parent-only and disposed actions cannot expose private results',async()=>{
  let calls=0;const response=deferred();
  const coach=actionHarness('coach',{getTrainingFees:()=>{calls++;return response.promise;}});
  await coach.loadFees();assert.equal(calls,0);
  const parent=actionHarness('parent',{getTrainingFees:()=>{calls++;return response.promise;}});
  const pending=parent.loadFees();parent.dispose();
  response.resolve({fees:[{id:'a',status:'issued',payment_status:'ready'}]});await pending;
  assert.equal(calls,1);assert.equal(parent.snapshot().fees,null);
});
