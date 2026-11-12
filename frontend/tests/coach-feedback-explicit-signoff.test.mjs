import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112150000_CoachFeedbackExplicitSignoff.cs');
const api=read('backend/src/LefteriaFc.Api/Messaging/MobileCoachFeedbackEndpoints.cs');
const store=read('backend/src/LefteriaFc.Infrastructure/Messaging/PostgresCoachFeedbackDraftStore.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const perms=read('ops/postgres/identity-app-permissions-check.sql');

test('coach signoff requires current owner of existing private draft',()=>{
 assert.match(sql,/v_draft\.coach_identity_id<>_identity/);
 assert.match(sql,/v_draft\.status<>'draft' OR v_draft\.version<>_version/);
 assert.match(sql,/WHERE id=_id FOR UPDATE/);
 assert.match(sql,/i\.id=_identity AND i\.normalized_phone=_phone/);
 assert.match(sql,/i\.role='coach' AND i\.is_active/);
 assert.match(sql,/l\.collection='staff' AND l\.kind='staff_own'/);
 assert.match(sql,/identity\.canonical_contact_bindings/);
 assert.match(sql,/s\.id=v_draft\.staff_id/);
 assert.match(sql,/club\.staff_teams/);
 assert.match(sql,/club\.staff_academy_groups/);
 assert.match(sql,/club\.academy_group_coaches/);
 assert.match(sql,/club\.player_academy_groups/);
 assert.match(sql,/migration\.staff_relationship_import_state/);
 assert.match(sql,/FOR SHARE OF i,l,b,s/);
});
test('human approved final text is immutable and never treated as delivered',()=>{
 assert.match(sql,/approved_by uuid REFERENCES identity\.mobile_identities/);
 assert.match(sql,/final_text text/);
 assert.match(sql,/final_text IS NOT NULL AND approved_at IS NOT NULL AND approved_by IS NOT NULL/);
 assert.match(sql,/v_draft\.final_text IS NOT NULL/);
 assert.match(sql,/SET status='approved',version=version\+1/);
 assert.match(sql,/approved_by=_identity/);
 assert.match(sql,/INSERT INTO audit\.security_events/);
 assert.match(sql,/'delivered',false/);
 assert.doesNotMatch(sql,/INSERT INTO club\.app_private_messages|INSERT INTO club\.app_chat_messages|INSERT INTO club\.mobile_push_subscriptions|SET status='sent'/);
});
test('API requires signed JWT and never takes a coach id from caller',()=>{
 assert.match(api,/MapPost\("\/api\/mobile\/coach\/feedback\/drafts\/\{id:guid\}\/approve"/);
 assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(api,/MobilePrincipal\.Read\(context\.User\)/);
 assert.match(api,/CoachFeedbackSignoffBody\(long Version,string\? FinalText\)/);
 assert.match(store,/actor\.Role!=MobileRole\.Coach/);
 assert.match(store,/SELECT club\.approve_mobile_coach_feedback_draft/);
 assert.match(store,/dto\.AuthorizedByCoach \|\| dto\.Delivered/);
 assert.doesNotMatch(api,/CoachName|CoachId|SendAsync|PublishAsync/);
});
test('signoff has only exact function EXECUTE, never mobile table write rights',()=>{
 const sig='club.approve_mobile_coach_feedback_draft(\n  uuid,text,text,timestamptz,uuid,bigint,text)';
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"app_role"'));
 assert.ok(perms.includes("'club.approve_mobile_coach_feedback_draft(uuid,text,text,timestamptz,uuid,bigint,text)'"));
});
