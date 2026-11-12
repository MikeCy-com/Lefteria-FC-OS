import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112160000_CoachFeedbackPrivateDelivery.cs');
const api=read('backend/src/LefteriaFc.Api/Messaging/MobileCoachFeedbackEndpoints.cs');
const store=read('backend/src/LefteriaFc.Infrastructure/Messaging/PostgresCoachFeedbackDraftStore.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const perms=read('ops/postgres/identity-app-permissions-check.sql');

test('explicit coach send is independent of final wording approval',()=>{
 assert.match(sql,/status IN \('draft','approved','sent'\)/);
 assert.match(sql,/recipient_identity_id uuid REFERENCES identity\.mobile_identities/);
 assert.match(sql,/status='sent'\)=\(recipient_identity_id IS NOT NULL AND sent_at IS NOT NULL\)/);
 assert.match(sql,/v_draft\.status<>'approved' OR v_draft\.version<>_version/);
 assert.match(sql,/v_draft\.approved_by<>_identity/);
 assert.match(sql,/club\.current_feedback_coach_assignment/);
 assert.match(sql,/l\.kind='player_own'/);
 assert.match(sql,/p\.id=v_draft\.player_id/);
 assert.match(sql,/SELECT i\.id INTO STRICT v_recipient/);
 assert.match(sql,/SET status='sent',version=version\+1/);
 assert.match(sql,/recipient_identity_id=v_recipient/);
 assert.match(sql,/'inbox_available',true,'read',false/);
 assert.doesNotMatch(sql,/INSERT INTO club\.app_private_messages|INSERT INTO club\.app_chat_messages|INSERT INTO club\.mobile_push_subscriptions/);
});
test('private inbox only serves sent words to canonical self player',()=>{
 assert.match(sql,/CREATE FUNCTION club\.read_mobile_player_coach_feedback/);
 assert.match(sql,/_role IS DISTINCT FROM 'player'/);
 assert.match(sql,/i\.id=_identity AND i\.normalized_phone=_phone/);
 assert.match(sql,/i\.role='player' AND i\.is_active/);
 assert.match(sql,/l\.collection='players' AND l\.kind='player_own'/);
 assert.match(sql,/identity\.canonical_contact_bindings/);
 assert.match(sql,/d\.recipient_identity_id=_identity/);
 assert.match(sql,/d\.player_id=v_player/);
 assert.match(sql,/d\.status='sent'/);
 assert.match(sql,/d\.approved_by=d\.coach_identity_id/);
 assert.match(sql,/LIMIT 20 OFFSET _offset/);
 assert.match(sql,/s\.name AS coach_name/);
 assert.doesNotMatch(sql,/d\.status='approved' OR|\brole='fan'\b|\brole='parent'\b/);
});
test('mobile API uses verified JWT role and never sends through AI worker',()=>{
 assert.match(api,/MapPost\("\/api\/mobile\/coach\/feedback\/drafts\/\{id:guid\}\/send"/);
 assert.match(api,/MapGet\("\/api\/mobile\/player\/coach-feedback"/);
 assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(api,/MobilePrincipal\.Read\(context\.User\)/);
 assert.match(store,/actor\.Role!=MobileRole\.Coach/);
 assert.match(store,/actor\.Role!=MobileRole\.Player/);
 assert.match(store,/SELECT club\.send_mobile_coach_feedback/);
 assert.match(store,/SELECT club\.read_mobile_player_coach_feedback/);
 assert.match(store,/dto\.Read/);
 assert.doesNotMatch(store,/LEFTERIA_AI_API_KEY|HubEducationProvider|auto_publish/);
});
test('mobile runtime cannot call the internal assignment helper directly',()=>{
 assert.match(sql,/REVOKE ALL ON FUNCTION club\.current_feedback_coach_assignment/);
 assert.match(grants,/REVOKE ALL ON FUNCTION club\.current_feedback_coach_assignment/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.send_mobile_coach_feedback/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_mobile_player_coach_feedback/);
 assert.match(perms,/Coach feedback send\/inbox function boundary is unsafe/);
 assert.match(perms,/has_function_privilege\(current_user,\s*'club\.current_feedback_coach_assignment/);
});
