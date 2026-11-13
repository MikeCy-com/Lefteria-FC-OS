import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {
 validHostedPayablUrl,validFanHubStatus,validFanHubCheckout,
 validFanHubLesson,validFanHubCompleted
} from '../src/lib/mobile/fan-payabl.ts';

import {validFanHubDeliverySchedule,validFanHubDeliveryInbox}
  from '../src/lib/mobile/fan-payabl.ts';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const checkout=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113170000_HubFanPayablCheckout.cs');
const lessonSql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113171000_HubFanPersonalEducation.cs');
const store=read('backend/src/LefteriaFc.Api/Hub/MobileFanPayablStore.cs');
const reader=read('backend/src/LefteriaFc.Api/Hub/MobileFanLearningStore.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Hub/MobileFanPayablEndpoints.cs');
const grant=read('ops/postgres/identity-app-grants.sql');
const permissions=read('ops/postgres/identity-app-permissions-check.sql');
const mobile=read('frontend/src/lib/mobile/controller.ts');
const ui=read('frontend/src/lib/mobile/components/MobileFanPayablHub.svelte');
const app=read('frontend/src/routes/app/[...path]/+page.svelte');
const org='lefteria_fc_academy';
const id='11111111-1111-4111-8111-111111111111';
const rev='22222222-2222-4222-8222-222222222222';
const active={organization_id:org,status:'active',audience_parent:true,
 audience_player:true,expires_at:'2090-12-01T00:00:00Z',checkout_url:null};
const next={organization_id:org,audience:'parent',next:{
 id,revision_id:rev,organization_id:org,audience:'parent',
 title:'New fan starts at lesson one',body:'Approved content',
 published_at:'2026-10-15T00:00:00Z'
}};

test('only Payabl sandbox hosted checkout destinations can be followed',()=>{
 assert.equal(validHostedPayablUrl('https://sandbox.payabl.com/pay/checkout'),true);
 assert.equal(validHostedPayablUrl('https://pay4.sandbox.payabl.com/checkout'),true);
 for(const url of ['http://sandbox.payabl.com/pay/checkout',
  'https://sandbox.payabl.com.attacker.test/pay',
  'https://sandbox.payabl.com@evil.test/pay',
  'javascript:alert(1)','https://evil.test/pay',
  'https://sandbox.payabl.com/pay#script'])
  assert.equal(validHostedPayablUrl(url),false,url);
 assert.equal(validFanHubStatus(active,org),true);
 assert.equal(validFanHubStatus({...active,expires_at:'2000-01-01T00:00:00Z'},org),false);
 assert.equal(validFanHubStatus({...active,status:'none'},org),true);
 assert.equal(validFanHubCheckout({id,status:'pending',created:true,
   payment_id:rev,amount_cents:1500,checkout_url:null}),true);
});
test('subscription is strictly sandbox priced, and never activates from redirect',()=>{
 assert.match(checkout,/CREATE TABLE hub\.fan_plan_prices/);
 assert.match(checkout,/is_enabled boolean NOT NULL DEFAULT false/);
 assert.match(checkout,/No prices are seeded/);
 assert.match(checkout,/monthly_price_cents INTO v_price/);
 assert.match(checkout,/AND is_enabled FOR SHARE/);
 assert.match(checkout,/INSERT INTO finance\.payment_requests/);
 assert.match(checkout,/'initiating'/);
 assert.match(checkout,/p\.status='paid' AND p\.captured_transaction_id IS NOT NULL/);
 assert.match(checkout,/provider_reference=p\.captured_transaction_id/);
 assert.match(checkout,/expires_at=p\.updated_at\+interval '30 days'/);
 assert.match(checkout,/p\.purpose='fan_hub_'\|\|link\.track/);
 assert.match(checkout,/p\.actor_id=_identity/);
 assert.doesNotMatch(checkout,/UPDATE finance\.payment_requests p[\s\S]{0,1000}status='paid'/);
 assert.match(store,/PayablGateway/);
 assert.match(store,/PayablNotification\.OrderId\(paymentId\)/);
 assert.match(store,/PayablSignature\.CallbackDigest\(nonce\)/);
 assert.match(store,/!body\.Consent/);
 assert.match(store,/!gateway\.Available/);
 assert.match(store,/return reserved with \{ CheckoutUrl=hosted\.CheckoutUrl \}/);
 assert.match(store,/do not retry/i);
});
test('fan subscription interest never confers guardian or player identity',()=>{
 assert.match(checkout,/_role IS DISTINCT FROM 'fan'/);
 assert.match(checkout,/i\.role='fan' AND i\.is_active/);
 assert.match(checkout,/consent_at/);
 assert.match(checkout,/_track IN \('parent','both'\)/);
 assert.match(checkout,/_track IN \('player','both'\)/);
 assert.match(lessonSql,/_audience NOT IN \('parent','player'\)/);
 assert.match(lessonSql,/s\.status='active'/);
 assert.match(lessonSql,/p\.status='paid'/);
 assert.match(lessonSql,/p\.captured_transaction_id=s\.provider_reference/);
 assert.match(lessonSql,/s\.expires_at>clock_timestamp\(\)/);
 assert.match(lessonSql,/s\.cancelled_at IS NULL/);
 assert.match(lessonSql,/AND c\.age_min IS NULL AND c\.age_max IS NULL/);
 assert.match(lessonSql,/c\.status='published'/);
 assert.match(lessonSql,/r\.text_review='passed' AND r\.visual_review='passed'/);
});
test('newly paid fan starts at oldest approved item, separately for each track',()=>{
 assert.match(lessonSql,/PRIMARY KEY\(identity_id,organization_id,audience,content_id,revision_id\)/);
 assert.match(lessonSql,/done\.identity_id=_identity/);
 assert.match(lessonSql,/done\.audience=_audience/);
 assert.match(lessonSql,/ORDER BY c\.published_at ASC,c\.id ASC LIMIT 1/);
 assert.match(lessonSql,/FOR UPDATE/);
 assert.match(lessonSql,/v_item->>'revision_id'/);
 assert.match(lessonSql,/INSERT INTO hub\.fan_learning_completions/);
 assert.doesNotMatch(lessonSql,/registration_date|registered_at|joined_at/);
 assert.equal(validFanHubLesson(next,org,'parent'),true);
 assert.equal(validFanHubLesson(next,org,'player'),false);
 assert.equal(validFanHubCompleted({organization_id:org,audience:'parent',
   completed_id:id,completed_revision_id:rev},next),true);
 assert.equal(validFanHubCompleted({organization_id:org,audience:'parent',
   completed_id:id,completed_revision_id:id},next),false);
});
test('mobile token role and identity changes reject old fan subscription results',()=>{
 assert.match(store,/actor\.Role!=MobileRole\.Fan/);
 assert.match(reader,/actor\.Role!=MobileRole\.Fan/);
 assert.match(endpoint,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(endpoint,/MapPost\("\/api\/mobile\/hub\/fan-subscription\/checkout"/);
 assert.match(endpoint,/MapGet\("\/api\/mobile\/hub\/fan-learning\/next"/);
 assert.match(endpoint,/MapPost\("\/api\/mobile\/hub\/fan-learning\/complete"/);
 assert.match(endpoint,/CacheControl="private, no-store"/);
 assert.match(mobile,/async getFanHubSubscription/);
 assert.match(mobile,/async startFanHubCheckout/);
 assert.match(mobile,/async getFanHubNextLesson/);
 assert.match(mobile,/async completeFanHubLesson/);
 assert.match(mobile,/state\.user\?\.id!==identity/);
 assert.match(ui,/consent/);
 assert.match(ui,/controller\.startFanHubCheckout/);
 assert.match(ui,/controller\.getFanHubNextLesson/);
 assert.match(app,/<MobileFanPayablHub \{controller\}/);
});
test('runtime has no direct private payment, subscription, or fan progress table grants',()=>{
 assert.match(grant,/GRANT EXECUTE ON FUNCTION hub\.reserve_mobile_fan_payabl/);
 assert.match(grant,/GRANT EXECUTE ON FUNCTION hub\.read_mobile_fan_subscription/);
 assert.match(grant,/GRANT EXECUTE ON FUNCTION hub\.read_mobile_fan_next_lesson/);
 assert.match(grant,/GRANT EXECUTE ON FUNCTION hub\.complete_mobile_fan_next_lesson/);
 assert.match(grant,/REVOKE ALL ON TABLE hub\.fan_learning_completions/);
 assert.match(grant,/REVOKE ALL ON TABLE hub\.fan_plan_prices,hub\.fan_checkout_links/);
 assert.match(permissions,/Payabl fan HUB runtime privileges unsafe/);
 assert.match(permissions,/Parent accessed fan-only paid material/);
 assert.doesNotMatch(grant,/GRANT SELECT ON TABLE finance\.payment_requests/);
});

test('paid fan notification schedule uses independent weekly clocks for both interests',()=>{
 const d=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113172000_HubFanWeeklyDelivery.cs');
 const worker=read('backend/src/LefteriaFc.Api/Hub/HubFanLessonDeliveryWorker.cs');
 const store=read('backend/src/LefteriaFc.Api/Hub/MobileFanLessonScheduleStore.cs');
 const workerGrants=read('ops/postgres/hub-fan-lesson-worker-grants.sql');
 const program=read('backend/src/LefteriaFc.Api/Program.cs');
 assert.match(d,/PRIMARY KEY\(identity_id,organization_id,audience\)/);
 assert.match(d,/UNIQUE\(identity_id,organization_id,audience,cycle\)/);
 assert.match(d,/cadence_days integer NOT NULL DEFAULT 7 CHECK\(cadence_days=7\)/);
 assert.match(d,/hub\.current_mobile_fan_lesson/);
 assert.match(d,/FOR UPDATE SKIP LOCKED/);
 assert.match(d,/v_now\+interval '7 days'/);
 assert.match(d,/EXCEPTION WHEN insufficient_privilege/);
 assert.match(d,/ON CONFLICT DO NOTHING/);
 assert.match(d,/'type','lesson_ready'/);
 assert.doesNotMatch(d,/INSERT INTO hub\.fan_lesson_delivery_inbox[\s\S]{0,250}body_text/);
 assert.match(worker,/LEFTERIA_FAN_LESSON_DELIVERY_ENABLED/);
 assert.match(worker,/LEFTERIA_FAN_LESSON_WORKER_CONNECTION/);
 assert.match(worker,/if\(!settings\.Enabled\)return/);
 assert.match(worker,/process_due_fan_lesson_schedules/);
 assert.match(workerGrants,/GRANT EXECUTE ON FUNCTION hub\.process_due_fan_lesson_schedules/);
 assert.match(workerGrants,/Fan lesson worker role permissions unsafe/);
 assert.match(store,/actor\.Role!=MobileRole\.Fan/);
 assert.match(program,/AddHostedService<HubFanLessonDeliveryWorker>/);
 assert.match(endpoint,/MapPost\("\/api\/mobile\/hub\/fan-learning\/schedule"/);
 assert.match(endpoint,/MapGet\("\/api\/mobile\/hub\/fan-learning\/deliveries"/);
 assert.match(grant,/GRANT EXECUTE ON FUNCTION hub\.start_mobile_fan_lesson_schedule/);
 assert.match(grant,/GRANT EXECUTE ON FUNCTION hub\.read_mobile_fan_lesson_deliveries/);
 assert.match(grant,/REVOKE ALL ON FUNCTION hub\.process_due_fan_lesson_schedules/);
 assert.match(permissions,/Fan lesson delivery runtime permissions unsafe/);
 assert.match(mobile,/async enrollFanWeeklyLessonDelivery/);
 assert.match(mobile,/async getFanWeeklyLessonDeliveries/);
 assert.match(ui,/controller\.enrollFanWeeklyLessonDelivery/);
 assert.match(ui,/controller\.getFanWeeklyLessonDeliveries/);
});
test('weekly fan notice cannot cross organizations, topic interests or dates',()=>{
 const schedule={organization_id:org,audience:'parent',
   started_at:'2026-11-01T00:00:00Z',
   next_due_at:'2026-11-08T00:00:00Z',cadence_days:7};
 assert.equal(validFanHubDeliverySchedule(schedule,org,'parent'),true);
 assert.equal(validFanHubDeliverySchedule(schedule,org,'player'),false);
 assert.equal(validFanHubDeliverySchedule({...schedule,cadence_days:2},org,'parent'),false);
 const item={id,queued_at:'2026-11-01T00:00:00Z',
   type:'lesson_ready',audience:'parent',content_id:rev};
 const inbox={organization_id:org,audience:'parent',items:[item]};
 assert.equal(validFanHubDeliveryInbox(inbox,org,'parent'),true);
 assert.equal(validFanHubDeliveryInbox(inbox,org,'player'),false);
 assert.equal(validFanHubDeliveryInbox({...inbox,items:[item,item]},org,'parent'),false);
 assert.equal(validFanHubDeliveryInbox({...inbox,items:[
  {...item,queued_at:'2090-11-01T00:00:00Z'}]},org,'parent'),false);
});
