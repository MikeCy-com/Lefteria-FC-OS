import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {createNotificationBadgeRefresher} from '../src/lib/mobile/notification-badge.ts';
import {stripTypeScriptTypes} from 'node:module';

const inbox=readFileSync(new URL('../src/lib/mobile/components/MobileAppNotificationInbox.svelte',import.meta.url),'utf8');

test('private inbox compiles and fetches fresh pages rather than retaining earlier private rows',()=>{
  compile(inbox,{filename:'MobileAppNotificationInbox.svelte',generate:'server'});
  compile(inbox,{filename:'MobileAppNotificationInbox.svelte',generate:'client'});
  assert.match(inbox,/busy=true;error=null;items=\[\];nextOffset=null;enabled=true;[\s\S]*?await controller\.getAppNotificationInbox\(offset\)/);
  assert.match(inbox,/if\(!alive\|\|marker!==request\|\|scope!==scopeKey\)return/);
  assert.match(inbox,/items=page\.enabled\?page\.notifications:\[\]/);
  assert.match(inbox,/nextOffset=page\.enabled&&page\.next_offset!==null&&page\.next_offset<=100000/);
  assert.doesNotMatch(inbox,/items=\[\.\.\.items|localStorage|sessionStorage|@html/);
});

test('invalid cursor, disabled preferences and failed pages cannot retain old inbox history',()=>{
  assert.match(inbox,/page\.next_offset!==null&&page\.next_offset<=offset/);
  assert.match(inbox,/previousOffsets=page\.enabled\?history:\[\]/);
  assert.match(inbox,/currentOffset=page\.enabled\?offset:0/);
  assert.match(inbox,/catch\{[\s\S]*?items=\[\];nextOffset=null;previousOffsets=\[\];currentOffset=0;[\s\S]*?onUnreadChange\?\.\(scope,0\)/);
});

test('page controls fetch previous or next offset and retry starts a fresh inbox',()=>{
  assert.match(inbox,/function refresh\(\)\{return loadPage\(0,\[\]\);\}/);
  assert.match(inbox,/return loadPage\(nextOffset,\[\.\.\.previousOffsets,currentOffset\]\)/);
  assert.match(inbox,/return loadPage\(previousOffsets\[previousOffsets\.length-1\]!,previousOffsets\.slice\(0,-1\)\)/);
  assert.match(inbox,/aria-label="Σελίδες ειδοποιήσεων"/);
  assert.match(inbox,/disabled=\{!previousOffsets\.length\}/);
  assert.match(inbox,/disabled=\{nextOffset===null\}/);
  assert.match(inbox,/class="outline retry" onclick=\{\(\)=>void refresh\(\)\}/);
});

const feed=readFileSync(new URL('../src/lib/mobile/components/MobileAppAnnouncements.svelte',import.meta.url),'utf8');
const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');

test('notification shell and inbox have one count owner and scope-bound responses',()=>{
  compile(feed,{filename:'MobileAppAnnouncements.svelte',generate:'client'});
  compile(route,{filename:'+page.svelte',generate:'client'});
  assert.match(route,/if\(!browser\|\|!scope\|\|owned\)return/);
  assert.match(route,/notificationScope===scope&&!inboxOwnsCount/);
  assert.match(route,/scope===notificationScope&&inboxOwnsCount/);
  assert.match(route,/onReadConfirmed=\{\(\)=>refreshNotificationBadge\(\)\}/);
  assert.match(route,/window\.clearInterval\(timer\)/);
  assert.match(inbox,/const marker=\+\+request,scope=scopeKey/);
  assert.match(inbox,/onUnreadChange\?\.\(scope,page\.unread_count\)/);
});
function deferred(){
  let resolve,reject;
  const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});
  return {promise,resolve,reject};
}
test('badge skips superseded responses and coalesces concurrent refreshes',async()=>{
  const first=deferred(),second=deferred(),counts=[];
  let calls=0;
  const badge=createNotificationBadgeRefresher(()=>++calls===1?first.promise:second.promise,n=>counts.push(n));
  const pending=badge.refresh();
  await badge.refresh();await badge.refresh();
  first.resolve(12);
  await Promise.resolve();
  assert.equal(calls,2);
  assert.deepEqual(counts,[]);
  second.resolve(9);
  await pending;
  assert.deepEqual(counts,[9]);
  assert.equal(calls,2);
});
test('disposed badge cannot publish a delayed private count or queued request',async()=>{
  const response=deferred(),counts=[];let calls=0;
  const badge=createNotificationBadgeRefresher(()=>{calls++;return response.promise;},n=>counts.push(n));
  const pending=badge.refresh();await badge.refresh();
  badge.dispose();response.resolve(12);await pending;await badge.refresh();
  assert.deepEqual(counts,[]);assert.equal(calls,1);
});
test('badge fails closed for rejected, malformed and opted-out counts',async()=>{
  const counts=[];
  const badge=createNotificationBadgeRefresher(async()=>{throw new Error('Denied');},n=>counts.push(n));
  await badge.refresh();
  for(const invalid of [-1,1000,2.2,NaN,undefined,0]){
    await createNotificationBadgeRefresher(async()=>invalid,n=>counts.push(n)).refresh();
  }
  assert.deepEqual(counts,[0,0,0,0,0,0,0]);
});
function readHarness(controller,confirmed){
  const start=feed.indexOf('  function readKey(');
  const end=feed.indexOf('  function seenOnScreen(',start);
  assert.ok(start>=0&&end>start);
  const handlers=stripTypeScriptTypes(feed.slice(start,end));
  return new Function('controller','onReadConfirmed',`
    const room={organization_id:'lefteria_fc_academy',group_id:'g1'};
    const id='ann_'+ 'a'.repeat(32);
    let selected=room,announcements=[{id}],alive=true,requestNumber=1,readError=null;
    const acknowledged=new Set(),acknowledgmentsInFlight=new Set();
    const document={visibilityState:'visible',hasFocus:()=>true};
    ${handlers}
    return {read:()=>acknowledgeRead(room,id,true),
      snapshot:()=>({readError,acknowledged:acknowledged.size,pending:acknowledgmentsInFlight.size}),
      dispose:()=>{alive=false;requestNumber++;},hide:()=>{document.visibilityState='hidden';},
      revoke:()=>{selected=null;announcements=[];}};
  `)(controller,confirmed);
}
test('explicit read is deduplicated and only confirmed server receipt refreshes badge',async()=>{
  const response=deferred();let reads=0,refreshes=0;
  const h=readHarness({acknowledgeAppAnnouncementRead:()=>{reads++;return response.promise;}},()=>refreshes++);
  const pending=h.read();await h.read();
  assert.equal(reads,1);assert.equal(refreshes,0);
  assert.equal(h.snapshot().pending,1);
  response.resolve(true);await pending;await h.read();
  assert.deepEqual(h.snapshot(),{readError:null,acknowledged:1,pending:0});
  assert.equal(refreshes,1);assert.equal(reads,1);
});
test('failed, hidden, revoked and disposed read actions never report a confirmed receipt',async()=>{
  let reads=0,refreshes=0;
  const failed=readHarness({acknowledgeAppAnnouncementRead:async()=>{reads++;return false;}},()=>refreshes++);
  await failed.read();
  assert.ok(failed.snapshot().readError);assert.equal(failed.snapshot().acknowledged,0);
  assert.equal(failed.snapshot().pending,0);
  failed.hide();await failed.read();
  const revoked=readHarness({acknowledgeAppAnnouncementRead:async()=>{reads++;return true;}},()=>refreshes++);
  revoked.revoke();await revoked.read();
  const response=deferred();
  const disposed=readHarness({acknowledgeAppAnnouncementRead:()=>response.promise},()=>refreshes++);
  const pending=disposed.read();disposed.dispose();response.resolve(true);await pending;
  assert.equal(disposed.snapshot().acknowledged,0);assert.equal(refreshes,0);assert.equal(reads,1);
});
