import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validActivityPage,activityDestination} from '../src/lib/mobile/notification-activity.ts';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125073000_MobileNotificationActivityFeed.cs');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const panel=read('frontend/src/lib/mobile/components/MobileNotificationActivity.svelte');
const group=read('frontend/src/lib/mobile/components/MobileGroupChat.svelte');
const privateChat=read('frontend/src/lib/mobile/components/MobilePrivateChat.svelte');
const contract=JSON.parse(read('contracts/openapi.json'));
const notice={kind:'announcement',entity_id:'ann_'+'a'.repeat(32),organization_id:'lefteria_fc_academy',
  group_id:'academy-15',conversation_id:null,occurred_at:'2026-11-25T08:00:00Z',title:'Training',group_name:'U15'};
const message={...notice,kind:'group_message',entity_id:'b8e46f68-5b63-4b93-99d1-926c216c33fe',title:null,
  occurred_at:'2026-11-25T07:00:00Z'};
const peer={...message,kind:'private_message',conversation_id:'4cb93bfb-4108-4ac9-a67b-c35346f7bc1d',
  group_name:null,occurred_at:'2026-11-25T06:00:00Z'};
const page=(items,next_offset=null)=>({items,next_offset});

test('chronological feed accepts scoped distinct rows and rejects leaked data or malformed pages',()=>{
  assert.equal(validActivityPage(page([notice,message,peer]),'all',0,true),true);
  assert.equal(validActivityPage(page([notice,message]),'all',0,false),true);
  for(const value of [page([peer,message]),page([notice,notice]),page([{...peer,content:'secret'}]),
    page([{...peer,title:'secret'}]),page([{...peer,conversation_id:null}]),
    page([{...message,group_id:'../admin'}]),page([notice],30)])
    assert.equal(validActivityPage(value,'all',0,true),false);
  assert.equal(validActivityPage(page([peer]),'all',0,false),false);
  assert.equal(validActivityPage(page([notice]),'group_message',0,true),false);
  assert.equal(validActivityPage(page([notice]),'all',0,true,'lefteria_fc'),false);
});
test('destinations use exact room or conversation IDs without sending message content',()=>{
  const a=new URL(activityDestination(notice),'https://example.test');
  const g=new URL(activityDestination(message),'https://example.test');
  const p=new URL(activityDestination(peer),'https://example.test');
  assert.equal(a.searchParams.get('notice'),notice.entity_id);
  assert.equal(g.searchParams.get('group'),message.group_id);
  assert.equal(g.searchParams.get('organization_id'),message.organization_id);
  assert.equal(p.searchParams.get('conversation'),peer.conversation_id);
  assert.equal(p.searchParams.get('chat_mode'),'private');
  assert.equal(p.searchParams.has('title'),false);
});
test('activity projection is read-only, preference scoped and excludes read or hidden versions',()=>{
  assert.match(sql,/club\.read_mobile_chat_groups\(_actor,_phone,_role,_expires\)/);
  assert.match(sql,/club\.mobile_private_pair_allowed\(_actor,_phone,_role,_expires/);
  assert.match(sql,/c\.status='accepted'/);
  assert.match(sql,/m\.created_at>c\.hidden_before/);
  assert.match(sql,/receipt\.message_version=m\.version/);
  assert.match(sql,/v_admin_enabled AND _kind IN/);
  assert.match(sql,/v_messages_enabled AND _kind IN/);
  assert.match(sql,/LIMIT 31 OFFSET _offset/);
  assert.doesNotMatch(sql,/INSERT INTO|UPDATE club|DELETE FROM|m\.content/);
  assert.deepEqual(contract.paths['/api/mobile/app/notifications/activity'].get.security,[{Mobile:[]}]);
});
test('UI navigation checks current directory and does not acknowledge merely opening feed',()=>{
  compile(panel,{filename:'MobileNotificationActivity.svelte',generate:'client'});
  compile(route,{filename:'+page.svelte',generate:'client'});
  assert.match(route,/await goto\(activityDestination\(item\)/);
  assert.match(group,/result\.find\(room=>room\.organization_id===initialOrganization&&room\.group_id===initialGroup\)/);
  assert.match(privateChat,/inbox\.find\(item=>item\.id===initialConversation&&item\.status==='accepted'\)/);
  assert.match(panel,/if\(!alive\|\|marker!==request\)return/);
  assert.doesNotMatch(panel,/acknowledge|localStorage|sessionStorage/);
});
