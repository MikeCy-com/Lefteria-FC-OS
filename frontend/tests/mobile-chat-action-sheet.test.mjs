import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';
import { availableChatActions, CHAT_REACTION_EMOJIS, validReaders } from '../src/lib/mobile/chat-actions.ts';
const read = path => readFileSync(new URL(path, import.meta.url),'utf8');
const sender=(overrides={})=>({
  kind:'message',role:'management',actorId:'actor-1',senderId:'actor-1',
  isDeleted:false,isParticipant:true,canReply:true,canEdit:true,
  canDelete:true,canReact:true,canSeeReaders:true,...overrides
});
const convo=(overrides={})=>({
  kind:'conversation',role:'parent',conversationKind:'private',isParticipant:true,
  canHideForSelf:true,...overrides
});
const notice=(overrides={})=>({
  kind:'announcement',role:'coach',isVisible:true,isDeleted:false,canReact:true,...overrides
});

test('own participant may edit, delete, react and inspect verified readers if management',()=>{
  assert.deepEqual(availableChatActions(sender()),['edit','delete','react','seen-by']);
  assert.deepEqual(availableChatActions(sender({role:'coach'})),['edit','delete','react']);
  assert.deepEqual(availableChatActions(sender({role:'parent'})),['edit','delete','react']);
  assert.deepEqual(availableChatActions(sender({canSeeReaders:false})),['edit','delete','react']);
  assert.deepEqual(availableChatActions(sender({senderId:'someone-else'})),['react']);
  assert.deepEqual(availableChatActions(sender({senderId:'someone-else',canReply:false})),[]);
});

test('cannot offer edits, reactions or readers for deleted/revoked/read-only messages',()=>{
  for(const change of [
    {isDeleted:true},{isParticipant:false},{actorId:''},
    {canReply:false,canSeeReaders:false},{canEdit:false,canDelete:false,canReact:false}
  ]){
    const actions=availableChatActions(sender(change));
    assert.ok(!actions.includes('edit') && !actions.includes('delete'),JSON.stringify(change));
    if(change.isDeleted || change.isParticipant===false) assert.deepEqual(actions,[]);
  }
  assert.deepEqual(availableChatActions(sender({canReply:false})),['seen-by']);
});

test('deleting private chat hides own history only; team channels cannot be deleted from menu',()=>{
  assert.deepEqual(availableChatActions(convo()),['delete-chat-for-me']);
  assert.deepEqual(availableChatActions(convo({conversationKind:'group'})),[]);
  assert.deepEqual(availableChatActions(convo({isParticipant:false})),[]);
  assert.deepEqual(availableChatActions(convo({canHideForSelf:false})),[]);
});

test('mobile announcement menu never exposes publication, editing or deletion',()=>{
  assert.deepEqual(availableChatActions(notice()),['react']);
  assert.deepEqual(availableChatActions(notice({isVisible:false})),[]);
  assert.deepEqual(availableChatActions(notice({isDeleted:true})),[]);
  assert.deepEqual(availableChatActions(notice({canReact:false})),[]);
  for(const action of availableChatActions(notice()))assert.equal(action,'react');
});

test('reaction choices are narrow and reader payload is bounded, distinct and name-only',()=>{
  assert.equal(new Set(CHAT_REACTION_EMOJIS).size,CHAT_REACTION_EMOJIS.length);
  assert.ok(CHAT_REACTION_EMOJIS.length<=10);
  assert.ok(validReaders({readers:[{id:'a',name:'A'}, {id:'b',name:'B'}]}));
  assert.ok(validReaders({readers:[]}));
  for(const bad of [
    {readers:[{id:'a',name:''}]},
    {readers:[{id:'a',name:'A'},{id:'a',name:'A'}]},
    {readers: Array.from({length:51},(_,i)=>({id:String(i),name:'Reader'}))},
    {readers:[{id:'a',name:'A'.repeat(181)}]},
    {readers:[{name:'Unknown'}]}, {readers:null}, null, []
  ])assert.equal(validReaders(bad),false);
});

test('long-press gesture matches original 550ms with scroll cancel and keyboard fallback',()=>{
  const source=read('../src/lib/mobile/components/MobileHoldAction.svelte');
  compile(source,{filename:'MobileHoldAction.svelte',generate:'server'});
  assert.match(source,/setTimeout\(\(\)=>\{/);
  assert.match(source, /},550\)/);
  assert.match(source,/Math\.hypot\([\s\S]*?>10\)cancel\(\)/);
  assert.match(source,/onpointercancel=\{cancel\}/);
  assert.match(source,/onpointerleave=\{cancel\}/);
  assert.match(source,/event\.key==='ContextMenu'/);
  assert.match(source,/event\.shiftKey && event\.key==='F10'/);
  assert.match(source,/data-chat-menu-trigger="true"/);
  assert.match(source,/min-height:44px/);
});

test('popup actions require actual callbacks and verified server results; focus is trapped',()=>{
  const source=read('../src/lib/mobile/components/MobileMessageActionMenu.svelte');
  compile(source,{filename:'MobileMessageActionMenu.svelte',generate:'server'});
  assert.match(source,/availableChatActions\(context\)\.filter/);
  assert.match(source,/if\(result===true\)onClose\(\)/);
  assert.match(source,/validReaders\(response,150\)/);
  assert.ok(validReaders({readers:Array.from({length:150},(_,i)=>({id:String(i),name:'Reader'}))},150));
  assert.match(source,/aria-modal="true"/);
  assert.match(source,/event\.key==='Escape'/);
  assert.match(source,/event\.key!=='Tab'/);
  assert.match(source,/data-testid="mobile-chat-seen-by"/);
  assert.match(source,/data-testid="mobile-chat-delete-confirm"/);
  assert.doesNotMatch(source,/fetch\(|deleteMessage\(|innerHTML|@html|localStorage/);
});
