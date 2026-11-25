import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {stripTypeScriptTypes} from 'node:module';
import {compile} from 'svelte/compiler';
import {validGroupChatUnreadSummary} from '../src/lib/mobile/group-chat.ts';

const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const chat=read('frontend/src/lib/mobile/components/MobileGroupChat.svelte');
const panel=read('frontend/src/lib/mobile/components/MobileGroupUnreadActions.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125070000_MobileGroupChatUnreadSummary.cs');
const api=read('backend/src/LefteriaFc.Api/Messaging/MobileGroupChatEndpoints.cs');
const grant=read('ops/postgres/identity-app-grants.sql');
const contract=JSON.parse(read('contracts/openapi.json'));

test('unread summary validates bounded exact response and disabled preference',()=>{
  for(const raw of [{enabled:true,unread_count:0},{enabled:true,unread_count:999},
    {enabled:false,unread_count:0}])assert.equal(validGroupChatUnreadSummary(raw),true);
  for(const raw of [null,{}, {enabled:false,unread_count:1},{enabled:true,unread_count:1000},
    {enabled:true,unread_count:-1},{enabled:true,unread_count:1.5},{enabled:true,unread_count:'2'},
    {enabled:true,unread_count:0,readers:[]}])assert.equal(validGroupChatUnreadSummary(raw),false);
});

test('unread summary is member scoped, version-specific, bounded and preference aware',()=>{
  assert.match(sql,/club\.read_mobile_chat_groups\(_actor,_phone,_role,_expires\)/);
  assert.match(sql,/notification_preferences @> '\{"messages":false\}'/);
  assert.match(sql,/NOT m\.is_deleted AND m\.sender_identity_id<>_actor/);
  assert.match(sql,/g\.value->>'organization_id'=m\.organization_id/);
  assert.match(sql,/g\.value->>'id'=m\.group_id/);
  assert.match(sql,/g\.value->>'kind'=m\.group_kind/);
  assert.match(sql,/r\.reader_identity_id=_actor/);
  assert.match(sql,/r\.message_version=m\.version/);
  assert.match(sql,/LIMIT 999/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.read_mobile_chat_unread_summary/);
  assert.doesNotMatch(sql,/INSERT INTO|UPDATE club|DELETE FROM/);
  assert.match(grant,/GRANT EXECUTE ON FUNCTION club\.read_mobile_chat_unread_summary/);
  assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
  assert.match(api,/CacheControl="private, no-store"/);
  assert.deepEqual(contract.paths['/api/mobile/chat/unread-summary'].get.security,[{Mobile:[]}]);
  assert.equal(contract.paths['/api/mobile/chat/unread-summary'].get.responses['200'].content['application/json'].schema.$ref,
    '#/components/schemas/MobileChatUnreadSummaryDto');
});

test('new UI stays separate from announcement counts and uses scoped controller',()=>{
  compile(panel,{filename:'MobileGroupUnreadActions.svelte',generate:'client'});
  compile(chat,{filename:'MobileGroupChat.svelte',generate:'client'});
  assert.match(controller,/async getGroupChatUnreadSummary\(/);
  assert.match(controller,/calendarScopeCurrent\(identity,bearer,owner\)/);
  assert.match(controller,/validGroupChatUnreadSummary\(result\)/);
  assert.match(panel,/if\(!alive\|\|marker!==request\)return/);
  assert.doesNotMatch(panel,/onUnreadChange|localStorage|sessionStorage/);
});

function harness(controller){
  const start=chat.indexOf('  async function refreshRooms('),end=chat.indexOf('  async function mutateMessage(',start);
  const handlers=stripTypeScriptTypes(chat.slice(start,end));
  return new Function('controller',`
    let requestNumber=0,alive=true,rooms=[],selected=null,messages=[],nextOffset=null;
    let directoryBusy=true,messageBusy=false,mutationBusy=false,sending=false;
    let menuMessage=null,error=null,sendError=null,draft='';
    const messageOf=e=>e.message;
    ${handlers}
    return {refreshRooms,chooseRoom,loadMessages,setDraft:value=>{draft=value;},
      snapshot:()=>({rooms,selected,messages,nextOffset,draft,error,messageBusy})};
  `)(controller);
}
const first={organization_id:'lefteria_fc',group_id:'a'};
const second={organization_id:'lefteria_fc_academy',group_id:'b'};
test('group change clears drafts, rejects unknown rooms and discards failed private pages',async()=>{
  let denied=false;
  const h=harness({getChatRooms:async()=>[first,second],
    getChatMessages:async(room,offset)=>{
      if(denied)throw new Error('Denied');
      return {messages:[{id:room.group_id}],next_offset:50};
    }});
  await h.refreshRooms();
  await h.chooseRoom(first);h.setDraft('Draft for A');
  await h.chooseRoom({...second});assert.equal(h.snapshot().selected,first);
  await h.chooseRoom(second);assert.equal(h.snapshot().draft,'');
  assert.deepEqual(h.snapshot().messages,[{id:'b'}]);
  await h.loadMessages(true);assert.deepEqual(h.snapshot().messages,[]);
  assert.equal(h.snapshot().nextOffset,null);
  await h.loadMessages(false);h.setDraft('Private draft');denied=true;
  await h.loadMessages(false);
  assert.equal(h.snapshot().error,'Denied');assert.equal(h.snapshot().draft,'');
  assert.deepEqual(h.snapshot().messages,[]);assert.equal(h.snapshot().messageBusy,false);
});

test('older room response cannot overwrite current room after rapid selection',async()=>{
  let resolve;
  const pending=new Promise(yes=>{resolve=yes;});
  const h=harness({getChatRooms:async()=>[first,second],getChatMessages:room=>
    room===first?pending:Promise.resolve({messages:[{id:'current'}],next_offset:null})});
  await h.refreshRooms();
  const old=h.chooseRoom(first);await h.chooseRoom(second);
  resolve({messages:[{id:'stale'}],next_offset:null});await old;
  assert.deepEqual(h.snapshot().messages,[{id:'current'}]);
});
