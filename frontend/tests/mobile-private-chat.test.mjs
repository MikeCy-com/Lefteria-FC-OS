import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validPrivateContact,validPrivateContacts,validPrivateConversation,
  validPrivateInbox,validPrivateMessage,validPrivatePage,validPrivateText
} from '../src/lib/mobile/private-chat.ts';

const read=path=>readFileSync(new URL(path,import.meta.url),'utf8');
const id='b8e46f68-5b63-4b93-99d1-926c216c33fe';
const other='4cb93bfb-4108-4ac9-a67b-c35346f7bc1d';
const conv={id,organization_id:'lefteria_fc_academy',group_id:'academy_15',
  partner_id:other,partner_name:'Coach',partner_role:'coach',
  status:'pending',is_initiator:true};
const message={id:other,conversation_id:id,sender_id:other,sender_name:'Coach',
  content:'Training notice',created_at:'2026-11-08T17:00:00Z',
  edited_at:null,is_deleted:false,version:1};

test('contacts never admit player, fan, duplicate IDs or raw phone fields',()=>{
  assert.ok(validPrivateContact({id,name:'Coach',role:'coach'}));
  assert.ok(validPrivateContacts({contacts:[{id,name:'Coach',role:'coach'}]}));
  assert.equal(validPrivateContacts({contacts:[{id,name:'Child',role:'player'}]}),false);
  assert.equal(validPrivateContacts({contacts:[{id,name:'Supporter',role:'fan'}]}),false);
  assert.equal(validPrivateContacts({contacts:[{id,name:'Coach',role:'coach'},
    {id,name:'Again',role:'coach'}]}),false);
  assert.equal(validPrivateContacts({contacts: Array(101).fill({id,name:'A',role:'coach'})}),false);
});

test('inbox and private message responses bind exact consent room IDs',()=>{
  assert.ok(validPrivateConversation(conv));
  assert.ok(validPrivateInbox({conversations:[conv]}));
  assert.equal(validPrivateConversation({...conv,partner_role:'player'}),false);
  assert.equal(validPrivateConversation({...conv,group_id:'../public'}),false);
  assert.equal(validPrivateConversation({...conv,status:'declined'}),false);
  assert.equal(validPrivateInbox({conversations:[conv,conv]}),false);
  assert.equal(validPrivateMessage(message,id),true);
  assert.equal(validPrivatePage({messages:[message],next_offset:null},id),true);
  assert.equal(validPrivateMessage(message,other),false);
  assert.equal(validPrivateMessage({...message,is_deleted:true,content:''},id),true);
  assert.equal(validPrivateMessage({...message,is_deleted:true},id),false);
  assert.equal(validPrivateMessage({...message,content:'\n'},id),false);
  assert.equal(validPrivateMessage({...message,version:0},id),false);
  assert.equal(validPrivatePage({messages:Array(51).fill(message),next_offset:null},id),false);
});

test('private text rejects hidden control characters and oversized bodies',()=>{
  assert.equal(validPrivateText('Γεια σου! ⚽'),true);
  for(const text of ['', ' ',' test ','Hello\nworld','\u0000', 'x'.repeat(10001)])
    assert.equal(validPrivateText(text),false);
});

test('private app compiles; private tab cannot reach fan or player identities',()=>{
  const source=read('../src/lib/mobile/components/MobilePrivateChat.svelte');
  compile(source,{filename:'MobilePrivateChat.svelte',generate:'server'});
  const route=read('../src/routes/app/[...path]/+page.svelte');
  compile(route,{filename:'+page.svelte',generate:'server'});
  assert.match(route,/role==='parent'\|\|role==='coach'\|\|role==='management'/);
  assert.match(route,/chatMode==='private'/);
  assert.match(route,/<MobilePrivateChat \{controller\} actorId=\{user.id\}/);
  assert.match(source,/await controller.invitePrivateContact\(room,target\)/);
  assert.match(source,/await controller.respondPrivateInvitation\(item.id,accept\)/);
  assert.match(source,/await controller.sendPrivateMessage\(item.id,body\)/);
  assert.match(source,/await controller.hidePrivateConversationForMe\(conv.id\)/);
  assert.match(source,/onHideChat=\{hide\}/);
  assert.doesNotMatch(source,/localStorage|sessionStorage|innerHTML|@html/);
});

test('private API requests use the current bearer session and exact backend confirmations',()=>{
  const source=read('../src/lib/mobile/controller.ts');
  assert.match(source,/async getPrivateContacts\(/);
  assert.match(source,/async invitePrivateContact\(/);
  assert.match(source,/async respondPrivateInvitation\(/);
  assert.match(source,/async getPrivateMessages\(/);
  assert.match(source,/async sendPrivateMessage\(/);
  assert.match(source,/async editPrivateMessage\(/);
  assert.match(source,/async deletePrivateMessage\(/);
  assert.match(source,/async hidePrivateConversationForMe\(/);
  assert.match(source,/result\.partner_id!==recipientId/);
  assert.match(source,/result\.version!==version\+1/);
  assert.match(source,/result\.conversation_id!==id/);
  assert.match(source,/if\(revision!==opRevision \|\| token!==sessionToken/);
  assert.doesNotMatch(source,/localStorage\.setItem\('private/);
});
