import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validPublishedHubNext,validPublishedHubCompletion}
  from '../src/lib/mobile/published-education.ts';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113150000_HubRecipientLearningProgress.cs');
const store=read('backend/src/LefteriaFc.Api/Hub/MobileHubPublishedStore.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/MobileHubPublishedEndpoints.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const checks=read('ops/postgres/identity-app-permissions-check.sql');
const controller=read('frontend/src/lib/mobile/controller.ts');
const journey=read('frontend/src/lib/mobile/components/MobilePersonalLearningJourney.svelte');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const first='11111111-1111-4111-8111-111111111111';
const revision='22222222-2222-4222-8222-222222222222';
const page={organization_id:'lefteria_fc_academy',audience:'parent',
  next:{id:first,revision_id:revision,organization_id:'lefteria_fc_academy',
    audience:'parent',title:'The first approved lesson',
    body:'A practical parent learning step',published_at:'2026-11-10T12:00:00Z',
    age_min:null,age_max:null}};

test('new enrolments start at the first approved lesson, not registration date or global newest',()=>{
 assert.match(sql,/ORDER BY c\.published_at ASC,c\.id ASC/);
 assert.doesNotMatch(sql,/enrolled_at|registered_at|joined_at|c\.published_at>=/);
 assert.match(sql,/WHERE done\.identity_id=_identity/);
 assert.match(sql,/done\.viewer_role=_role AND done\.audience=_role/);
 assert.match(sql,/done\.organization_id=_owner/);
 assert.match(sql,/done\.revision_id=r\.id/);
 assert.match(sql,/NOT EXISTS \(/);
 assert.match(sql,/LIMIT 1/);
});
test('approved lessons only and no age guessing, AI self-publish or fan bypass',()=>{
 assert.match(sql,/c\.status='published'/);
 assert.match(sql,/c\.approved_by IS NOT NULL/);
 assert.match(sql,/r\.text_review='passed' AND r\.visual_review='passed'/);
 assert.match(sql,/c\.age_min IS NULL AND c\.age_max IS NULL/);
 assert.match(sql,/_role NOT IN \('parent','player'\)/);
 assert.match(sql,/m\.moderation<>'reviewed'/);
 assert.doesNotMatch(sql,/UPDATE hub\.content|SET status='published'|INSERT INTO hub\.fan_subscriptions/);
});
test('completion is explicit and strictly sequential, identity locked and version-bound',()=>{
 assert.match(sql,/CREATE FUNCTION hub\.complete_mobile_next_education/);
 assert.match(sql,/FOR UPDATE/);
 assert.match(sql,/v_page:=hub\.current_mobile_education_lesson/);
 assert.match(sql,/v_lesson->>'id'/);
 assert.match(sql,/v_lesson->>'revision_id'/);
 assert.match(sql,/INSERT INTO hub\.education_completions/);
 assert.match(sql,/PRIMARY KEY\(identity_id,organization_id,viewer_role,audience,content_id,revision_id\)/);
 assert.match(sql,/viewer_role='fan' OR viewer_role=audience/);
 assert.match(sql,/ALTER TABLE hub\.education_completions ENABLE ROW LEVEL SECURITY/);
});
test('app runtime gets only narrow SQL functions, no raw progress or fan access',()=>{
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.read_mobile_next_education/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.complete_mobile_next_education/);
 assert.match(grants,/REVOKE ALL ON TABLE hub\.education_completions/);
 assert.match(grants,/REVOKE ALL ON FUNCTION hub\.current_mobile_education_lesson/);
 assert.match(checks,/Fan bypassed private learning gate/);
 assert.match(checks,/Fan bypassed private learning completion gate/);
 assert.match(api,/MapGet\("\/api\/mobile\/hub\/learning\/next"/);
 assert.match(api,/MapPost\("\/api\/mobile\/hub\/learning\/complete"/);
 assert.match(api,/CacheControl="private, no-store"/);
 assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(store,/CheckLearningScope\(actor,organization\)/);
});
test('individual next page and exact completion are validated in the controller',()=>{
 assert.equal(validPublishedHubNext(page,'parent','lefteria_fc_academy'),true);
 assert.equal(validPublishedHubNext(page,'player','lefteria_fc_academy'),false);
 assert.equal(validPublishedHubNext({...page,next:{...page.next,age_min:8}},'parent','lefteria_fc_academy'),false);
 assert.equal(validPublishedHubNext({...page,next:null},'parent','lefteria_fc_academy'),true);
 const result={organization_id:page.organization_id,audience:page.audience,
   completed_id:first,completed_revision_id:revision};
 assert.equal(validPublishedHubCompletion(result,page),true);
 assert.equal(validPublishedHubCompletion({...result,completed_revision_id:first},page),false);
 assert.match(controller,/async getNextHubLesson/);
 assert.match(controller,/async completeNextHubLesson/);
 assert.match(controller,/state\.user\?\.id!==actorId/);
 assert.match(journey,/controller\.getNextHubLesson\(organization\)/);
 assert.match(journey,/controller\.completeNextHubLesson\(submitted\)/);
 assert.match(route,/MobilePersonalLearningJourney \{controller\} viewerRole=\{role\}/);
 assert.doesNotMatch(journey,/localStorage|sessionStorage|Payabl|fanInterest/);
});
