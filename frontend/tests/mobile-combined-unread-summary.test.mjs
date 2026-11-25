import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validNotificationUnreadSummary} from '../src/lib/mobile/notification-inbox.ts';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125072000_MobileNotificationUnreadSummary.cs');
const contract=JSON.parse(read('contracts/openapi.json'));
const destination=(unread_count,enabled=true)=>({unread_count,enabled});
const snapshot=(a,g,p,unread_count)=>({announcements:a,group_messages:g,private_messages:p,unread_count});
test('combined summary enforces exact totals, destination preference and cap',()=>{
  assert.equal(validNotificationUnreadSummary(snapshot(destination(2),destination(3),destination(4),9),true),true);
  assert.equal(validNotificationUnreadSummary(snapshot(destination(999),destination(999),destination(1),999),true),true);
  for(const value of [snapshot(destination(2),destination(3),destination(4),8),
    snapshot(destination(2,false),destination(0),destination(0),2),
    snapshot(destination(1000),destination(0),destination(0),999),
    snapshot(destination(0),destination(0),destination(0),-1),
    {...snapshot(destination(0),destination(0),destination(0),0),messages:[]}])
    assert.equal(validNotificationUnreadSummary(value,true),false);
});
test('player summary must never contain enabled private messaging',()=>{
  assert.equal(validNotificationUnreadSummary(snapshot(destination(3),destination(2),destination(0,false),5),false),true);
  assert.equal(validNotificationUnreadSummary(snapshot(destination(3),destination(2),destination(0,true),5),false),false);
  assert.equal(validNotificationUnreadSummary(snapshot(destination(3),destination(2),destination(1),6),false),false);
});
test('backend reuses authorized count functions and returns no message rows',()=>{
  assert.match(sql,/club\.read_mobile_chat_groups\(_actor,_phone,_role,_expires\)/);
  assert.match(sql,/club\.read_mobile_app_notification_inbox\(_actor,_phone,_role,_expires,0\)/);
  assert.match(sql,/club\.read_mobile_chat_unread_summary\(_actor,_phone,_role,_expires\)/);
  assert.match(sql,/_role IN \('parent','coach','management'\) AND jsonb_array_length\(v_groups\)>0/);
  assert.match(sql,/least\(v_ann_count\+v_group_count\+v_private_count,999\)/);
  assert.deepEqual(contract.paths['/api/mobile/app/notifications/unread-summary'].get.security,[{Mobile:[]}]);
});
test('badge remains unified on inbox and receives only confirmed receipt invalidations',()=>{
  compile(route,{filename:'+page.svelte',generate:'client'});
  assert.match(route,/controller\.getNotificationUnreadSummary\(\)/);
  assert.match(route,/notificationSnapshot\?\.scope===notificationScope/);
  assert.match(route,/if\(scope&&scope===notificationScope\)refreshNotificationBadge\(\)/);
  assert.doesNotMatch(route,/notificationCount=count|inboxOwnsCount/);
  assert.match(route,/MobilePrivateChat[^\n]*onReadConfirmed/);
  assert.match(route,/MobileGroupChat[^\n]*onReadConfirmed/);
});
