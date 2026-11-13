import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const enrollment=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113190000_HubScopedLearningEnrollment.cs');
const dispatch=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113191000_HubDueWeeklyLessonDispatch.cs');
const store=read('backend/src/LefteriaFc.Api/Hub/MobileWeeklyLearningStore.cs');
const endpoints=read('backend/src/LefteriaFc.Api/Hub/MobileWeeklyLearningEndpoints.cs');
const worker=read('backend/src/LefteriaFc.Api/Hub/HubWeeklyLearningDispatchWorker.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const scheduler=read('ops/postgres/hub-weekly-scheduler-grants.sql');

test('every enrollment begins independently at first week, while repeats preserve position',()=>{
 assert.match(enrollment,/ON CONFLICT \(identity_id,organization_id,audience\)\s*DO NOTHING/);
 assert.match(enrollment,/next_due_at,delivery_week,status\)/);
 assert.match(enrollment,/clock_timestamp\(\),clock_timestamp\(\),1,'active'/);
 assert.match(enrollment,/WHERE identity_id=_identity AND organization_id=_owner/);
 assert.match(enrollment,/A cancelled track cannot silently re-enable/);
 assert.doesNotMatch(enrollment,/registered_at|enrolled_after|current_week_of_year/i);
});

test('player and parent access is tied to canonical roster and current phone delegation',()=>{
 assert.match(enrollment,/JOIN identity\.canonical_contact_bindings b/);
 assert.match(enrollment,/l\.kind='player_own'/);
 assert.match(enrollment,/l\.kind='guardian'/);
 assert.match(enrollment,/identity\.current_phone_granted_children/);
 assert.match(enrollment,/p\.organization_id=_owner AND p\.is_active/);
 assert.match(enrollment,/p\.is_archived AND NOT p\.is_transferred/);
 assert.match(enrollment,/_role='fan' AND _audience IN \('fan_parent','fan_player'\)/);
 assert.match(enrollment,/hub\.fan_delivery_entitled/);
});

test('dispatcher serializes due enrollment and never skips unavailable reviewed content',()=>{
 assert.match(dispatch,/FOR UPDATE OF e SKIP LOCKED/);
 assert.match(dispatch,/v_enrollment\.next_due_at<=clock_timestamp\(\)/);
 assert.match(dispatch,/hub\.authorize_mobile_learning_enrollment/);
 assert.match(dispatch,/c\.status='published' AND c\.approved_by IS NOT NULL/);
 assert.match(dispatch,/rr\.text_review='passed' AND rr\.visual_review='passed'/);
 assert.match(dispatch,/c\.age_min IS NULL AND c\.age_max IS NULL/);
 assert.match(dispatch,/ORDER BY c\.published_at ASC,c\.id ASC/);
 assert.match(dispatch,/IF v_content IS NULL OR v_revision IS NULL THEN/);
 assert.match(dispatch,/ON CONFLICT DO NOTHING/);
 assert.match(dispatch,/e\.delivery_week=e\.delivery_week\+1/);
 assert.match(dispatch,/interval '7 days'/);
 assert.match(dispatch,/e\.status='active'/);
 assert.doesNotMatch(dispatch,/INSERT INTO hub\.fan_subscriptions|UPDATE hub\.fan_subscriptions/);
});

test('private inbox rechecks fan entitlement and published content, never trusts stored delivery alone',()=>{
 assert.match(enrollment,/CREATE FUNCTION hub\.read_mobile_weekly_learning/);
 assert.match(enrollment,/PERFORM hub\.authorize_mobile_learning_enrollment/);
 assert.match(enrollment,/d\.state='delivered'/);
 assert.match(enrollment,/c\.status='published' AND c\.approved_by IS NOT NULL/);
 assert.match(enrollment,/r\.text_review='passed' AND r\.visual_review='passed'/);
 assert.match(enrollment,/media\.moderation<>'reviewed'/);
 assert.match(enrollment,/LIMIT 20/);
 assert.match(store,/UnmappedMemberHandling\.Disallow/);
 assert.match(store,/ValidateAudience\(actor,organization,audience\)/);
 assert.match(store,/result\.Deliveries\.All/);
});

test('no background dispatch or payment collection by default; staging role only',()=>{
 assert.match(worker,/HubLearningScheduler:Enabled/);
 assert.match(worker,/if\(connection is null\)return/);
 assert.match(worker,/environment\.IsStaging\(\)/);
 assert.match(worker,/HubLearningScheduler:IsolatedStaging/);
 assert.match(worker,/value\.Username!="lefteria_hub_scheduler"/);
 assert.match(worker,/dispatch_due_weekly_learning\(@max\)/);
 assert.match(worker,/PeriodicTimer\(TimeSpan\.FromMinutes\(10\)\)/);
 assert.match(scheduler,/current_database\(\) !~\* '\(_staging\|_preview\|_test\)\$'/);
 assert.match(scheduler,/GRANT EXECUTE ON FUNCTION hub\.dispatch_due_weekly_learning/);
 assert.match(scheduler,/REVOKE ALL ON ALL TABLES IN SCHEMA hub/);
 assert.match(grants,/REVOKE ALL ON FUNCTION hub\.dispatch_due_weekly_learning/);
 assert.doesNotMatch(grants,/GRANT EXECUTE ON FUNCTION hub\.dispatch_due_weekly_learning/);
 assert.match(program,/AddHostedService<HubWeeklyLearningDispatchWorker>/);
});

test('authenticated private mobile API offers per-audience enrollment and inbox only',()=>{
 assert.match(endpoints,/MapPost\("\/api\/mobile\/hub\/weekly\/enroll"/);
 assert.match(endpoints,/MapGet\("\/api\/mobile\/hub\/weekly\/inbox"/);
 assert.match(endpoints,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(endpoints,/CacheControl="private, no-store"/);
 assert.match(endpoints,/Request\.Query\["organization_id"\]\.Count!=1/);
 assert.match(endpoints,/Request\.Query\["audience"\]\.Count!=1/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.enroll_mobile_weekly_learning/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.read_mobile_weekly_learning/);
 assert.doesNotMatch(grants,/GRANT SELECT ON hub\.learning_delivery_outbox/);
 assert.match(program,/AddScoped<MobileWeeklyLearningStore>/);
 assert.match(program,/MapMobileWeeklyLearning\(\)/);
});
