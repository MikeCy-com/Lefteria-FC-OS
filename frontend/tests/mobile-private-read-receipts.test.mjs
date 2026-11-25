import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {stripTypeScriptTypes} from 'node:module';
import {compile} from 'svelte/compiler';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const chat=read('frontend/src/lib/mobile/components/MobilePrivateChat.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const panel=read('frontend/src/lib/mobile/components/MobileGroupUnreadActions.svelte');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125071000_MobilePrivateChatReadReceipts.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const contract=JSON.parse(read('contracts/openapi.json'));

test('private receipt storage requires current consent, peer version and visible history',()=>{
  assert.match(sql,/PRIMARY KEY\(message_id,reader_identity_id\)/);
  assert.match(sql,/c\.status='accepted'/);
  assert.match(sql,/_actor IN\(c\.requested_by,c\.invited_identity_id\)/);
  assert.match(sql,/m\.sender_identity_id<>_actor/);
  assert.match(sql,/m\.version=_version AND NOT m\.is_deleted/);
  assert.match(sql,/club\.mobile_private_pair_allowed\(_actor,_phone,_role,_expires/);
  assert.match(sql,/m\.created_at<=v\.hidden_before/);
  assert.match(sql,/FOR SHARE OF m/);
  assert.match(sql,/ON CONFLICT\(message_id,reader_identity_id\) DO UPDATE/);
  assert.match(sql,/RETURNING message_id INTO v_id;\s*IF NOT FOUND/);
  assert.match(sql,/r\.message_version=m\.version/);
  assert.match(sql,/m\.created_at>p\.hidden_before/);
  assert.match(sql,/notification_preferences @> '\{"messages":false\}'/);
  assert.match(sql,/LIMIT 999/);
  assert.match(grants,/REVOKE ALL ON TABLE club\.app_private_read_receipts FROM PUBLIC, :"app_role"/);
  assert.ok(grants.lastIndexOf('GRANT EXECUTE ON FUNCTION club.read_mobile_private_unread_summary')<grants.lastIndexOf('COMMIT;'));
  assert.deepEqual(contract.paths['/api/mobile/private/unread-summary'].get.security,[{Mobile:[]}]);
  assert.deepEqual(contract.paths['/api/mobile/private/conversations/{conversationId}/messages/{messageId}/seen'].post.security,[{Mobile:[]}]);
});
test('private UI uses confirmed receipts and adult-only unread navigation',()=>{
  compile(chat,{filename:'MobilePrivateChat.svelte',generate:'client'});
  compile(panel,{filename:'MobileGroupUnreadActions.svelte',generate:'client'});
  compile(route,{filename:'+page.svelte',generate:'client'});
  assert.match(chat,/document\.visibilityState!=='visible'\|\|!document\.hasFocus\(\)/);
  assert.match(chat,/entry\.intersectionRatio>=0\.75/);
  assert.match(chat,/await controller\.acknowledgePrivateRead\(conv.id,message.id,message.version\)/);
  assert.match(chat,/use:seenOnScreen=\{message\}/);
  assert.match(chat,/onclick=\{\(\)=>void confirmRead\(message\)\}/);
  assert.match(controller,/result\.conversation_id!==conversation/);
  assert.match(controller,/result\.message_id!==message/);
  assert.match(controller,/result\.version!==version/);
  assert.match(route,/\{#if canOpenPrivateChat\(role\)\}\s*<MobileGroupUnreadActions \{controller\} privateMode=\{true\}/);
  assert.doesNotMatch(chat,/localStorage|sessionStorage/);
});
function harness(controller){
  const start=chat.indexOf('  function receiptKey('),end=chat.indexOf('  function seenOnScreen(',start);
  const handlers=stripTypeScriptTypes(chat.slice(start,end));
  return new Function('controller',`
    const actorId='me',peer={id:'m',conversation_id:'c',sender_id:'other',version:1,is_deleted:false};
    let alive=true,sequence=0,selected={id:'c',status:'accepted'},messages=[peer],messageLoading=false;
    let receiptError=null;const onReadConfirmed=()=>{};
    const acknowledged=new Set(),receiptPending=new Set();
    const document={visibilityState:'visible',hasFocus:()=>true};
    ${handlers}
    return {read:message=>confirmRead(message??peer),peer,
      hide:()=>{document.visibilityState='hidden';},show:()=>{document.visibilityState='visible';},
      change:()=>{sequence++;selected={id:'new',status:'accepted'};messages=[];},
      edit:()=>{messages=[{...peer,version:2}];return messages[0];},
      snapshot:()=>({ack:[...acknowledged],pending:receiptPending.size,receiptError})};
  `)(controller);
}
test('receipt is not credited before confirmation; duplicate clicks share pending guard',async()=>{
  let finish,calls=0;
  const h=harness({acknowledgePrivateRead:()=>{calls++;return new Promise(resolve=>{finish=resolve;});}});
  const first=h.read();
  await h.read();
  assert.equal(calls,1);
  assert.deepEqual(h.snapshot().ack,[]);
  finish(true);await first;
  assert.deepEqual(h.snapshot().ack,['c/m:1']);
  assert.equal(h.snapshot().pending,0);
});
test('hidden, own, stale and deleted messages cannot submit receipts',async()=>{
  let calls=0;
  const h=harness({acknowledgePrivateRead:async()=>{calls++;return true;}});
  h.hide();await h.read();h.show();
  await h.read({...h.peer,sender_id:'me'});
  await h.read({...h.peer,is_deleted:true});
  const current=h.edit();await h.read();assert.equal(calls,0);
  await h.read(current);assert.deepEqual(h.snapshot().ack,['c/m:2']);
});
test('late or failed acknowledgement cannot claim read in another conversation',async()=>{
  let finish;
  const h=harness({acknowledgePrivateRead:()=>new Promise(resolve=>{finish=resolve;})});
  const pending=h.read();h.change();finish(true);await pending;
  assert.deepEqual(h.snapshot().ack,[]);
  const failed=harness({acknowledgePrivateRead:async()=>false});
  await failed.read();assert.deepEqual(failed.snapshot().ack,[]);
  assert.ok(failed.snapshot().receiptError);assert.equal(failed.snapshot().pending,0);
});
