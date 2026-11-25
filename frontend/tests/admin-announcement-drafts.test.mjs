import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {stripTypeScriptTypes} from 'node:module';
import {compile} from 'svelte/compiler';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108190000_AdminAnnouncementDrafts.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminAnnouncementDraftEndpoints.cs');
const ui=read('frontend/src/routes/admin/announcements/+page.svelte');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const boundary=read('ops/postgres/admin-announcement-drafts-boundary.sql');
const nav=read('frontend/src/lib/admin/module-catalog.ts');

test('announcement draft save is group-bound and audit atomic without dispatch',()=>{
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/club\.teams t/);
  assert.match(sql,/club\.academy_groups g/);
  assert.match(sql,/INSERT INTO club\.admin_announcement_drafts/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(sql,/CHECK \(status='draft'\)/);
  assert.doesNotMatch(sql,/INSERT INTO (chat|messages|outbox|notifications)\./);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.create_admin_announcement_draft/);
  assert.match(boundary,/has_any_column_privilege\(auth_role,'club\.admin_announcement_drafts'/);
});
test('announcement admin page keeps draft saves and owner-scoped confirmed publication',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role,capability\)/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(ui,/data-testid="announcement-draft-form"/);
  assert.match(ui,/\/api\/admin\/football\/audiences/);
  assert.match(ui,/\/api\/admin\/communications\/announcement-drafts/);
  assert.match(ui,/method:'POST',credentials:'same-origin',cache:'no-store'/);
  assert.match(ui,/saved\.status!=='draft'/);
  assert.match(nav,/id: 'announcements'/);
  assert.match(ui,/announcement-publish/);
  assert.match(ui,/announcement-remove/);
  assert.match(ui,/app-announcements/);
  assert.match(ui,/publishDraft\(draft\)/);
  assert.match(ui,/removeAnnouncement\(item\)/);
  assert.match(ui,/window\.confirm\(/);
  assert.doesNotMatch(ui,/\/api\/admin\/messages|sendPush|@html/);
});

test('app notices use separate audited publish storage; mobile readers see no drafts or user contact rows',()=>{
  const publish=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108270000_AppAnnouncementPublishing.cs');
  const mobile=read('backend/src/LefteriaFc.Api/Messaging/MobileAppAnnouncementEndpoints.cs');
  const boundary=read('ops/postgres/app-announcement-publishing-boundary.sql');
  assert.match(publish,/CREATE TABLE club\.app_announcements/);
  assert.match(publish,/CREATE FUNCTION club\.publish_admin_app_announcement/);
  assert.match(publish,/CREATE FUNCTION club\.remove_admin_app_announcement/);
  assert.match(publish,/CREATE FUNCTION club\.read_mobile_app_announcements/);
  assert.match(publish,/UPDATE club\.admin_announcement_drafts/);
  assert.match(publish,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(publish,/club\.read_mobile_chat_groups\(_actor,_phone,_role,_expires\)/);
  assert.match(publish,/a\.removed_at IS NULL/);
  assert.match(publish,/INSERT INTO audit\.security_events/);
  assert.match(mobile,/RequireAuthorization\(IdentityServices\.Policy\)/);
  assert.match(boundary,/has_any_column_privilege\(mobile_role,'club\.app_announcements'/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.publish_admin_app_announcement/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_admin_app_announcements/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.remove_admin_app_announcement/);
  assert.doesNotMatch(publish,/sendPush|twilio|outbox|email/i);
});

test('mobile announcements compile and are restricted to current member tab and server confirmations',()=>{
  const feed=read('frontend/src/lib/mobile/components/MobileAppAnnouncements.svelte');
  compile(feed,{filename:'MobileAppAnnouncements.svelte',generate:'server'});
  const route=read('frontend/src/routes/app/[...path]/+page.svelte');
  compile(route,{filename:'+page.svelte',generate:'server'});
  assert.match(route,/activeTab === 'announcements' && role !== 'fan'/);
  assert.match(route,/<MobileAppAnnouncements \{controller\}/);
  assert.match(feed,/controller\.getChatRooms\(\)/);
  assert.match(feed,/controller\.getAppAnnouncements\(room,0\)/);
  assert.doesNotMatch(feed,/localStorage|sessionStorage|innerHTML|@html/);
});

test('app notice reactions use current roster scope, audited private storage and server-confirmed mobile controls',()=>{
  const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108280000_AppAnnouncementEmojiReactions.cs');
  const endpoint=read('backend/src/LefteriaFc.Api/Messaging/MobileAppAnnouncementEndpoints.cs');
  const grants=read('ops/postgres/identity-app-grants.sql');
  const feed=read('frontend/src/lib/mobile/components/MobileAppAnnouncements.svelte');
  const controller=read('frontend/src/lib/mobile/controller.ts');
  const boundary=read('ops/postgres/app-announcement-publishing-boundary.sql');
  compile(feed,{filename:'MobileAppAnnouncements.svelte',generate:'server'});
  assert.match(migration,/CREATE TABLE club\.app_announcement_reactions/);
  assert.match(migration,/club\.read_mobile_chat_groups\(_actor,_phone,_role,_expires\)/);
  assert.match(migration,/a\.removed_at IS NULL/);
  assert.match(migration,/FOR UPDATE OF a/);
  assert.match(migration,/ON CONFLICT DO NOTHING/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.match(endpoint,/SetMobileAppAnnouncementReaction/);
  assert.match(endpoint,/RequireAuthorization\(IdentityServices\.Policy\)/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.set_mobile_app_announcement_reaction/);
  assert.match(boundary,/Raw announcement reaction participants leaked/);
  assert.match(controller,/validAppAnnouncementReactionSnapshot/);
  assert.match(feed,/controller\.setAppAnnouncementReaction/);
  assert.match(feed,/CHAT_REACTION_EMOJIS/);
  assert.doesNotMatch(feed,/localStorage|sessionStorage|@html/);
});

test('announcement read acknowledgement requires focused card and admin names use a separate guarded endpoint',()=>{
  const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108290000_AppAnnouncementReadReceipts.cs');
  const mobile=read('frontend/src/lib/mobile/components/MobileAppAnnouncements.svelte');
  const controller=read('frontend/src/lib/mobile/controller.ts');
  const admin=read('frontend/src/routes/admin/announcements/+page.svelte');
  const adminApi=read('backend/src/LefteriaFc.Api/Audiences/AdminAnnouncementDraftEndpoints.cs');
  const authGrants=read('ops/postgres/web-admin-auth-grants.sql');
  const mobileGrants=read('ops/postgres/identity-app-grants.sql');
  compile(mobile,{filename:'MobileAppAnnouncements.svelte',generate:'server'});
  compile(admin,{filename:'+page.svelte',generate:'server'});
  assert.match(migration,/CREATE TABLE club\.app_announcement_read_receipts/);
  assert.match(migration,/club\.is_current_mobile_chat_reader/);
  assert.match(migration,/club\.app_announcement_admin_actor/);
  assert.match(migration,/ON CONFLICT DO NOTHING/);
  assert.match(mobile,/IntersectionObserver/);
  assert.match(mobile,/intersectionRatio>=0\.75/);
  assert.match(mobile,/document\.hasFocus\(\)/);
  assert.match(mobile,/use:seenOnScreen/);
  assert.match(controller,/acknowledgeAppAnnouncementRead/);
  assert.match(adminApi,/GetAdminAppAnnouncementReaders/);
  assert.match(admin,/readerDialog\?\.showModal\(\)/);
  assert.match(mobileGrants,/GRANT EXECUTE ON FUNCTION club\.ack_mobile_app_announcement_read/);
  assert.match(authGrants,/GRANT EXECUTE ON FUNCTION club\.read_admin_app_announcement_readers/);
});

test('private announcement media uses sanitized JPEG, versioned draft PDF, current member delivery and no public object URL',()=>{
  const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108300000_AppAnnouncementPrivateMedia.cs');
  const guard=read('backend/src/LefteriaFc.Api/WebAdmin/WebAdminRequestGuards.cs');
  const admin=read('frontend/src/routes/admin/announcements/+page.svelte');
  const mobile=read('frontend/src/lib/mobile/components/MobileAppAnnouncements.svelte');
  const banner=read('frontend/src/lib/mobile/components/MobileAnnouncementBanner.svelte');
  const controller=read('frontend/src/lib/mobile/controller.ts');
  const appGrants=read('ops/postgres/identity-app-grants.sql');
  const authGrants=read('ops/postgres/web-admin-auth-grants.sql');
  compile(admin,{filename:'+page.svelte',generate:'server'});
  compile(mobile,{filename:'MobileAppAnnouncements.svelte',generate:'server'});
  compile(banner,{filename:'MobileAnnouncementBanner.svelte',generate:'server'});
  assert.match(migration,/CREATE TABLE club\.app_announcement_media/);
  assert.match(migration,/CREATE UNIQUE INDEX ux_app_announcement_single_banner/);
  assert.match(migration,/club\.app_announcement_admin_actor/);
  assert.match(migration,/club\.read_mobile_chat_groups/);
  assert.match(migration,/a\.removed_at IS NULL/);
  assert.match(migration,/SET version=version\+1/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.match(guard,/mediaPath \? 4 \* 1024 \* 1024 \+ 128 \* 1024/);
  assert.match(appGrants,/GRANT EXECUTE ON FUNCTION club\.read_mobile_app_announcement_media/);
  assert.match(authGrants,/GRANT EXECUTE ON FUNCTION club\.write_admin_app_announcement_media/);
  assert.match(controller,/validAppAnnouncementMediaDownload/);
  assert.match(mobile,/controller\.getAppAnnouncementMedia/);
  assert.match(banner,/URL\.revokeObjectURL/);
  assert.doesNotMatch(mobile,/localStorage|sessionStorage|@html|public_attachments/);
});

test('app-only notification inbox rechecks membership and preferences instead of producing device push',()=>{
  const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108310000_AppAnnouncementNotificationInbox.cs');
  const memberEndpoint=read('backend/src/LefteriaFc.Api/Messaging/MobileAppAnnouncementEndpoints.cs');
  const grants=read('ops/postgres/identity-app-grants.sql');
  const boundary=read('ops/postgres/app-announcement-publishing-boundary.sql');
  const notifications=read('frontend/src/lib/mobile/components/MobileAppNotificationInbox.svelte');
  const routes=read('frontend/src/routes/app/[...path]/+page.svelte');
  const controller=read('frontend/src/lib/mobile/controller.ts');
  const validator=read('frontend/src/lib/mobile/notification-inbox.ts');
  compile(notifications,{filename:'MobileAppNotificationInbox.svelte',generate:'server'});
  compile(routes,{filename:'+page.svelte',generate:'server'});
  assert.match(migration,/CREATE TABLE club\.app_announcement_notification_events/);
  assert.match(migration,/PRIMARY KEY/);
  assert.match(migration,/AFTER INSERT ON club\.app_announcements/);
  assert.match(migration,/ON CONFLICT \(announcement_id\) DO NOTHING/);
  assert.match(migration,/club\.read_mobile_chat_groups/);
  assert.match(migration,/notification_preferences @>/);
  assert.match(migration,/app_announcement_read_receipts/);
  assert.match(migration,/a\.removed_at IS NULL/);
  assert.match(migration,/IF v_opted_out THEN/);
  assert.match(memberEndpoint,/GetMobileAppNotificationInbox/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_mobile_app_notification_inbox/);
  assert.match(boundary,/Raw table access leaked|outbox\/raw table access leaked|outbox\/raw table/i);
  assert.match(validator,/validAppNotificationInbox/);
  assert.match(validator,/new Set\(items\.map/);
  assert.match(controller,/getAppNotificationInbox/);
  assert.match(routes,/notificationCount=\$state\(0\)/);
  assert.match(routes,/MobileAppNotificationInbox/);
  assert.match(routes,/openNotification/);
  assert.match(notifications,/onUnreadChange/);
  assert.doesNotMatch(notifications,/Notification\.requestPermission|PushManager|serviceWorker\.register|localStorage|sessionStorage|@html/);
});

test('Web Push remains opt-in, generic, private and disabled without operator keys',()=>{
  const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108320000_DeviceWebPushSubscriptions.cs');
  const endpoints=read('backend/src/LefteriaFc.Api/Messaging/MobileWebPushEndpoints.cs');
  const sender=read('backend/src/LefteriaFc.Api/Messaging/WebPushDelivery.cs');
  const worker=read('backend/src/LefteriaFc.Api/Messaging/WebPushDispatchWorker.cs');
  const control=read('frontend/src/lib/mobile/components/MobileWebPushControls.svelte');
  const inbox=read('frontend/src/lib/mobile/components/MobileAppNotificationInbox.svelte');
  const browser=read('frontend/src/lib/mobile/web-push.ts');
  const serviceWorker=read('frontend/static/mobile/push-service-worker.js');
  const grants=read('ops/postgres/identity-app-grants.sql');
  compile(control,{filename:'MobileWebPushControls.svelte',generate:'server'});
  compile(inbox,{filename:'MobileAppNotificationInbox.svelte',generate:'server'});
  assert.match(migration,/CREATE TABLE club\.mobile_push_subscriptions/);
  assert.match(migration,/CREATE TABLE club\.app_announcement_push_attempts/);
  assert.match(migration,/UNIQUE\(announcement_id,subscription_id\)/);
  assert.match(migration,/FOR UPDATE OF d SKIP LOCKED/);
  assert.match(migration,/club\.is_current_mobile_chat_reader/);
  assert.match(migration,/notification_preferences @>/);
  assert.match(migration,/lease_token=gen_random_uuid/);
  assert.match(sender,/WebPush:Enabled/);
  assert.match(sender,/ECDsa\.Create/);
  assert.match(sender,/TrustedPushHost/);
  assert.match(sender,/HttpCompletionOption\.ResponseHeadersRead/);
  assert.match(worker,/RecheckAsync/);
  assert.match(worker,/FinishAsync/);
  assert.match(endpoints,/SubscribeMobileWebPushDevice/);
  assert.match(endpoints,/UnsubscribeMobileWebPushDevice/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.save_mobile_push_subscription/);
  assert.match(browser,/Notification\.requestPermission\(\)/);
  assert.match(browser,/userVisibleOnly:true/);
  assert.match(browser,/existing\.unsubscribe\(\)/);
  assert.match(control,/enableBrowserPush/);
  assert.match(inbox,/MobileWebPushControls/);
  assert.match(serviceWorker,/self\.registration\.showNotification/);
  assert.match(serviceWorker,/\/app\?tab=notifications/);
  assert.doesNotMatch(serviceWorker,/Bearer|Authorization|p256dh|auth_secret|announcement_id|\.title|\.body|fetch\(/);
  assert.doesNotMatch(browser,/localStorage|sessionStorage|requestPermission\(\).*onMount/);
});


// Run the component's actual asynchronous directory/feed functions against
// deferred API fakes. SSR alone cannot exercise the onMount request handoff.
function announcementDirectoryHarness(controller, initial = {}) {
  const feed = read('frontend/src/lib/mobile/components/MobileAppAnnouncements.svelte');
  const start = feed.indexOf('  async function loadDirectory(){');
  const end = feed.indexOf('  function seenOnScreen(', start);
  assert.ok(start >= 0 && end > start, 'Announcement request handlers must exist');
  const handlers = stripTypeScriptTypes(feed.slice(start, end));
  return new Function('controller', 'initial', `
    let rooms=[],selected=null,announcements=[],nextOffset=null;
    let loading=true,busy=false,error=null,reactionFor=null,reacting=null,downloading=null;
    let previousOffsets=[],currentOffset=0,targetMessage=null;
    let requestNumber=0,alive=true;
    const {initialOrganization=null,initialGroup=null,initialAnnouncementId=null}=initial;
    const fault=error=>error.message;
    const tick=async()=>{};
    const document={getElementById:()=>null};
    ${handlers}
    return {loadDirectory,chooseRoom,loadMore,loadPrevious,
      snapshot:()=>({loading,busy,error,rooms,selected,announcements,nextOffset,previousOffsets,currentOffset,targetMessage}),
      setAction:action=>{reacting=action==='reaction'?'notice':null;downloading=action==='download'?'file':null;},
      dispose:()=>{alive=false;requestNumber++;}};
  `)(controller, initial);
}
function deferredAnnouncementResult() {
  let resolve, reject;
  const promise = new Promise((yes,no) => { resolve=yes;reject=no; });
  return {promise,resolve,reject};
}

test('announcement auto-selection ends directory loading and enables refresh after feed success or failure', async () => {
  const first={organization_id:'lefteria_fc_academy',group_id:'group-one',name:'First'};
  const second={organization_id:'lefteria_fc_academy',group_id:'group-two',name:'Second'};
  for (const requested of [false,true]) {
    for (const failed of [false,true]) {
      const pending=deferredAnnouncementResult();
      const calls=[];
      const harness=announcementDirectoryHarness({
        getChatRooms:async()=>requested?[first,second]:[first],
        getAppAnnouncements:(room,offset)=>{calls.push([room,offset]);return pending.promise;}
      },requested?{initialOrganization:second.organization_id,initialGroup:second.group_id}:{});
      await harness.loadDirectory();
      const room=requested?second:first;
      assert.equal(harness.snapshot().loading,false,'The directory is already loaded');
      assert.equal(harness.snapshot().busy,true,'Only the feed is still loading');
      assert.equal(harness.snapshot().selected,room);
      assert.deepEqual(calls,[[room,0]]);
      if(failed)pending.reject(new Error('Feed unavailable'));
      else pending.resolve({announcements:[],next_offset:null});
      await new Promise(resolve=>setImmediate(resolve));
      assert.equal(harness.snapshot().loading,false);
      assert.equal(harness.snapshot().busy,false,'Refresh must become available');
      assert.equal(harness.snapshot().error,failed?'Feed unavailable':null);
    }
  }
});

test('announcement directory ignores stale and disposed results without selecting an unauthorized room', async () => {
  const old=deferredAnnouncementResult(),current=deferredAnnouncementResult();
  let calls=0,feedCalls=0;
  const harness=announcementDirectoryHarness({
    getChatRooms:()=>++calls===1?old.promise:current.promise,
    getAppAnnouncements:()=>{feedCalls++;throw new Error('Unexpected feed call');}
  });
  const prior=harness.loadDirectory(),latest=harness.loadDirectory();
  old.resolve([{organization_id:'lefteria_fc',group_id:'old',name:'Old'}]);
  await prior;
  assert.equal(harness.snapshot().loading,true);
  assert.deepEqual(harness.snapshot().rooms,[]);
  current.resolve([]);
  await latest;
  assert.equal(harness.snapshot().loading,false);
  assert.equal(feedCalls,0);
  const disposed=deferredAnnouncementResult();
  const unmounted=announcementDirectoryHarness({
    getChatRooms:()=>disposed.promise,
    getAppAnnouncements:()=>{feedCalls++;throw new Error('Unexpected feed call');}
  });
  const request=unmounted.loadDirectory();
  unmounted.dispose();
  disposed.resolve([{organization_id:'lefteria_fc',group_id:'old',name:'Old'}]);
  await request;
  assert.deepEqual(unmounted.snapshot().rooms,[]);
  assert.equal(feedCalls,0);
});

const noticeId='ann_'+ 'a'.repeat(32);
const announcementRoom={organization_id:'lefteria_fc_academy',group_id:'group-one',name:'First'};
const announcementTarget={initialOrganization:announcementRoom.organization_id,
  initialGroup:announcementRoom.group_id,initialAnnouncementId:noticeId};

test('notification deep link searches only authorized room and finds an older page',async()=>{
  const calls=[];
  const harness=announcementDirectoryHarness({
    getChatRooms:async()=>[announcementRoom],
    getAppAnnouncements:async(room,offset)=>{
      calls.push([room,offset]);
      return offset===0?{announcements:[{id:'newer'}],next_offset:50}:
        {announcements:[{id:noticeId}],next_offset:null};
    }
  },announcementTarget);
  await harness.loadDirectory();
  await new Promise(resolve=>setImmediate(resolve));
  assert.deepEqual(calls,[[announcementRoom,0],[announcementRoom,50]]);
  assert.deepEqual(harness.snapshot().announcements,[{id:noticeId}]);
  assert.equal(harness.snapshot().targetMessage,null);
  assert.equal(harness.snapshot().busy,false);
  assert.deepEqual(harness.snapshot().previousOffsets,[0]);
  await harness.loadPrevious();
  assert.deepEqual(harness.snapshot().announcements,[{id:'newer'}]);
  assert.equal(calls.length,3);
});

test('missing target group does not silently open a different authorized room',async()=>{
  let calls=0;
  const harness=announcementDirectoryHarness({
    getChatRooms:async()=>[announcementRoom],
    getAppAnnouncements:async()=>{calls++;return {announcements:[],next_offset:null};}
  },{...announcementTarget,initialGroup:'revoked-group'});
  await harness.loadDirectory();
  assert.equal(harness.snapshot().selected,null);
  assert.equal(calls,0);
  assert.ok(harness.snapshot().targetMessage);
  await harness.chooseRoom({...announcementRoom});
  assert.equal(calls,0,'Room must come from current directory');
  await harness.chooseRoom(announcementRoom);
  assert.equal(calls,1);
});

test('deep link scan is bounded, pagination is serialized, and revoked page clears private data',async()=>{
  const offsets=[];
  const harness=announcementDirectoryHarness({
    getChatRooms:async()=>[announcementRoom],
    getAppAnnouncements:async(room,offset)=>{
      offsets.push(offset);
      if(offset===250)throw new Error('Access changed');
      return {announcements:[{id:'page-'+offset}],next_offset:offset+50};
    }
  },announcementTarget);
  await harness.loadDirectory();
  await new Promise(resolve=>setImmediate(resolve));
  assert.deepEqual(offsets,[0,50,100,150,200]);
  assert.deepEqual(harness.snapshot().announcements,[{id:'page-200'}]);
  assert.ok(harness.snapshot().targetMessage);
  for(const action of ['reaction','download']){
    harness.setAction(action);
    await harness.loadMore();
    await harness.loadPrevious();
    await harness.chooseRoom(announcementRoom);
  }
  assert.equal(offsets.length,5);
  harness.setAction(null);
  await harness.loadMore();
  assert.deepEqual(offsets,[0,50,100,150,200,250]);
  assert.deepEqual(harness.snapshot().announcements,[]);
  assert.deepEqual(harness.snapshot().previousOffsets,[]);
  assert.equal(harness.snapshot().nextOffset,null);
  assert.equal(harness.snapshot().busy,false);
  assert.equal(harness.snapshot().error,'Access changed');
});

test('non-advancing announcement cursor is rejected without exposing the page',async()=>{
  const harness=announcementDirectoryHarness({
    getChatRooms:async()=>[announcementRoom],
    getAppAnnouncements:async()=>({announcements:[{id:'invalid'}],next_offset:0})
  });
  await harness.loadDirectory();
  await new Promise(resolve=>setImmediate(resolve));
  assert.deepEqual(harness.snapshot().announcements,[]);
  assert.equal(harness.snapshot().nextOffset,null);
  assert.ok(harness.snapshot().error);
});
