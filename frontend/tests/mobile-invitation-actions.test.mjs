import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {stripTypeScriptTypes} from 'node:module';
import {privateChatMode,pendingPrivateInvitations,pendingFamilyInvitationCount} from '../src/lib/mobile/invitation-actions.ts';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const panel=read('lib/mobile/components/MobileInvitationActions.svelte');
const route=read('routes/app/[...path]/+page.svelte');
const chat=read('lib/mobile/components/MobilePrivateChat.svelte');
const controller=read('lib/mobile/controller.ts');
const owner='lefteria_fc_academy';

test('incoming consent requests are not accepted conversations or outgoing requests',()=>{
  const rows=[
    {organization_id:owner,status:'pending',is_initiator:false},
    {organization_id:owner,status:'pending',is_initiator:true},
    {organization_id:owner,status:'accepted',is_initiator:false}
  ];
  assert.equal(pendingPrivateInvitations(rows,'parent',null),1);
  assert.equal(pendingPrivateInvitations(rows,'coach',null),1);
  assert.equal(pendingPrivateInvitations(rows,'management',owner),1);
  assert.throws(()=>pendingPrivateInvitations(rows,'management',null));
  assert.throws(()=>pendingPrivateInvitations(rows,'management','lefteria_fc'));
  for(const role of ['fan','player'])assert.throws(()=>pendingPrivateInvitations(rows,role,null));
});

test('private chat URL mode cannot grant fan or player access',()=>{
  for(const role of ['parent','coach','management']){
    assert.equal(privateChatMode(role,'private'),'private');
    assert.equal(privateChatMode(role,'groups'),'groups');
  }
  for(const role of ['fan','player',null])assert.equal(privateChatMode(role,'private'),'groups');
  assert.equal(privateChatMode('parent','invalid'),'groups');
});

test('family invitation count validates live expiry and bounded recipient contract',()=>{
  const now=Date.parse('2026-11-25T05:00:00Z');
  const row={id:'b8e46f68-5b63-4b93-99d1-926c216c33fe',organization_id:owner,
    status:'pending',created_at:'2026-11-24T05:00:00Z',expires_at:'2026-11-26T05:00:00Z'};
  assert.equal(pendingFamilyInvitationCount({items:[]},now),0);
  assert.equal(pendingFamilyInvitationCount({items:[row]},now),1);
  for(const raw of [null,{items:[row,row]},{items:[{...row,status:'accepted'}]},
    {items:[{...row,expires_at:'2026-11-25T05:00:00Z'}]},
    {items:[{...row,organization_id:'other'}]},{items:[{...row,id:'bad'}]}])
    assert.throws(()=>pendingFamilyInvitationCount(raw,now));
});

test('invitation UI uses existing guarded APIs and no unread count or payment side effects',()=>{
  compile(panel,{filename:'MobileInvitationActions.svelte',generate:'client'});
  compile(panel,{filename:'MobileInvitationActions.svelte',generate:'server'});
  compile(route,{filename:'+page.svelte',generate:'client'});
  compile(chat,{filename:'MobilePrivateChat.svelte',generate:'client'});
  assert.match(panel,/role==='fan'\?await controller\.getPendingFamilyInvitationCount\(\)/);
  assert.match(panel,/canOpenPrivateChat\(role\)\?await controller\.getPendingPrivateInvitationCount\(\)/);
  assert.match(panel,/if\(!alive\|\|marker!==request\)return/);
  assert.doesNotMatch(panel,/onUnreadChange|partner_name|localStorage|sessionStorage/);
  assert.match(route,/onOpen=\{\(\)=>openTab\('profile'\)\}/);
  assert.match(route,/onOpen=\{\(\)=>openChatMode\('private'\)\}/);
  assert.match(route,/privateChatMode\(role,page\.url\.searchParams\.get\('chat_mode'\)\)/);
  assert.match(controller,/state\.recipientDecliningId \|\| state\.recipientAcceptingId \|\| state\.recipientStatus === 'loading'/);
  assert.match(chat,/selected=item;messages=\[\];nextOffset=null;messageError=null;draft=''/);
  assert.match(chat,/request!==contactSequence\|\|roomKey!==key/);
  assert.match(chat,/!contacts\.some\(c=>c\.id===target\)/);
  assert.match(chat,/page\.next_offset!==null&&page\.next_offset<=offset/);
  assert.match(chat,/messages=\[\];nextOffset=null;draft='';messageError=fault\(e\)/);
});

function deferred(){
  let resolve,reject;
  const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});
  return {promise,resolve,reject};
}
function chatHarness(controller){
  const range=(start,end)=>chat.slice(chat.indexOf(start),chat.indexOf(end,chat.indexOf(start)));
  const handlers=stripTypeScriptTypes(range('  async function loadContacts(','  function changeRoom(')+
    range('  async function openConversation(','  async function send('));
  return new Function('controller',`
    let alive=true,sequence=0,contactSequence=0,roomKey='',contactLoading=false;
    let contacts=[],contactId='',error=null,selected=null,messages=[],nextOffset=null;
    let messageError=null,menuMessage=null,menuConversation=null,messageLoading=false,busy=false,draft='';
    const fault=e=>e.message;
    ${handlers}
    return {loadContacts,openConversation,reloadMessages,
      setRoom:room=>{roomKey=room.organization_id+':'+room.group_id;},
      setDraft:value=>{draft=value;},
      snapshot:()=>({contacts,contactLoading,selected,messages,nextOffset,messageError,messageLoading,draft})};
  `)(controller);
}
test('same-room contact request races cannot overwrite a newer authorized directory',async()=>{
  const old=deferred(),latest=deferred();let calls=0;
  const room={organization_id:owner,group_id:'g1'};
  const h=chatHarness({getPrivateContacts:()=>++calls===1?old.promise:latest.promise});
  h.setRoom(room);
  const a=h.loadContacts(room),b=h.loadContacts(room);
  latest.resolve([{id:'current'}]);await b;old.resolve([{id:'stale'}]);await a;
  assert.deepEqual(h.snapshot().contacts,[{id:'current'}]);
  assert.equal(h.snapshot().contactLoading,false);
});
test('conversation changes clear draft and invalid or denied pagination removes displayed data',async()=>{
  const a={id:'a',status:'accepted'},b={id:'b',status:'accepted'};
  let denied=false;
  const h=chatHarness({getPrivateMessages:async(id,offset)=>{
    if(denied)throw new Error('Denied');
    return {messages:[{id:id+'-message'}],next_offset:offset===0?50:50};
  }});
  await h.openConversation(a);h.setDraft('Draft for A');
  await h.openConversation(b);assert.equal(h.snapshot().draft,'');
  assert.deepEqual(h.snapshot().messages,[{id:'b-message'}]);
  await h.reloadMessages(true);
  assert.deepEqual(h.snapshot().messages,[]);assert.equal(h.snapshot().nextOffset,null);
  await h.openConversation(b);h.setDraft('Private draft');denied=true;
  await h.reloadMessages();
  assert.equal(h.snapshot().messageError,'Denied');
  assert.deepEqual(h.snapshot().messages,[]);assert.equal(h.snapshot().draft,'');
  assert.equal(h.snapshot().messageLoading,false);
});
