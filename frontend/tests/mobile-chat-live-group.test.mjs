import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validChatDirectory,validPage,validMessage,validRoom,validTextMessage} from '../src/lib/mobile/group-chat.ts';

const read=path=>readFileSync(new URL(path,import.meta.url),'utf8');
const academy={organization_id:'lefteria_fc_academy',group_id:'acad_1',kind:'academy_group',name:'U-15'};
const team={organization_id:'lefteria_fc',group_id:'first_team',kind:'team',name:'FC'};

test('only source-backed FC/Academy room shapes pass validation',()=>{
  assert.equal(validRoom(academy),true);
  assert.equal(validRoom(team),true);
  assert.equal(validRoom({...academy,organization_id:'lefteria_fc'}),false);
  assert.equal(validRoom({...team,group_id:'../admin'}),false);
  const directory={ready:true,groups:[
    {organization_id:academy.organization_id,id:academy.group_id,kind:academy.kind,name:academy.name},
    {organization_id:team.organization_id,id:team.group_id,kind:team.kind,name:team.name}
  ]};
  assert.equal(validChatDirectory(directory),true);
  assert.equal(validChatDirectory({...directory,ready:false}),false);
  assert.equal(validChatDirectory({...directory,groups:[directory.groups[0],directory.groups[0]]}),false);
});

test('message snapshots never accept a cross-room payload',()=>{
  const message={id:'b8e46f68-5b63-4b93-99d1-926c216c33fe',sender_id:'4cb93bfb-4108-4ac9-a67b-c35346f7bc1d',
    sender_name:'Coach',organization_id:academy.organization_id,group_id:academy.group_id,
    content:'Training notice',created_at:'2026-11-08T14:20:00Z',
    edited_at:null,is_deleted:false,version:1};
  assert.ok(validMessage(message,academy));
  assert.ok(validPage({messages:[message],next_offset:null},academy));
  assert.equal(validPage({messages:[message],next_offset:100001},academy),true);
  assert.equal(validPage({messages:[message],next_offset:-1},academy),false);
  assert.equal(validMessage(message,team),false);
  assert.equal(validMessage({...message,is_deleted:true,content:''},academy),true);
  assert.equal(validMessage({...message,is_deleted:true},academy),false);
  assert.equal(validMessage({...message,version:0},academy),false);
  assert.equal(validMessage({...message,content:''},academy),false);
  assert.equal(validPage({messages:Array(51).fill(message),next_offset:null},academy),false);
});

test('text messages bound to one line, UTF-8-safe text limit and no control characters',()=>{
  assert.equal(validTextMessage('Γεια σου! ⚽'),true);
  for(const message of ['', ' ', '\n', ' test ', 'a\n b','a'.repeat(10001)])
    assert.equal(validTextMessage(message),false);
});

test('mobile chat screen compiles and never renders unverified messages optimistically',()=>{
  const source=read('../src/lib/mobile/components/MobileGroupChat.svelte');
  compile(source,{filename:'MobileGroupChat.svelte',generate:'server'});
  assert.match(source,/await controller\.getChatRooms\(\)/);
  assert.match(source,/await controller\.getChatMessages\(room/);
  assert.match(source,/const sent=await controller\.sendChatMessage\(room,body\)/);
  assert.match(source,/messages=\[sent,/);
  assert.doesNotMatch(source,/localStorage|sessionStorage|innerHTML|@html/);
});

test('authenticated app tab holds mobile chat within bearer session, no public website chat',()=>{
  const controller=read('../src/lib/mobile/controller.ts');
  const route=read('../src/routes/app/[...path]/+page.svelte');
  assert.match(controller,/if\(revision!==opRevision \|\| token!==sessionToken/);
  assert.match(controller,/cache:'no-store'/);
  assert.match(route,/activeTab === 'chat' && role !== 'fan'/);
  assert.match(route,/<MobileGroupChat \{controller\} actorId=\{user.id\}/);
});
