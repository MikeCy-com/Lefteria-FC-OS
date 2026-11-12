import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = p => readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112130000_CoachVerifiedFeedbackDrafts.cs');
const endpoints=read('backend/src/LefteriaFc.Api/Messaging/MobileCoachFeedbackEndpoints.cs');
const store=read('backend/src/LefteriaFc.Infrastructure/Messaging/PostgresCoachFeedbackDraftStore.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const perms=read('ops/postgres/identity-app-permissions-check.sql');

test('coach feedback is an unpublished author-authenticated draft, not AI impersonation',()=>{
 assert.match(sql,/CREATE TABLE club\.coach_feedback_drafts/);
 assert.match(sql,/status varchar\(12\) NOT NULL DEFAULT 'draft'/);
 assert.match(sql,/CONSTRAINT ck_coach_feedback_status CHECK \(status='draft'\)/);
 assert.match(sql,/coach_identity_id uuid NOT NULL/);
 assert.match(sql,/staff_id varchar\(128\) NOT NULL/);
 assert.match(sql,/player_id varchar\(128\) NOT NULL/);
 assert.match(sql,/observation varchar\(500\) NOT NULL/);
 assert.match(sql,/focus_point varchar\(500\) NOT NULL/);
 assert.match(sql,/language IN \('el','en'\)/);
 assert.match(sql,/ALTER TABLE club\.coach_feedback_drafts ENABLE ROW LEVEL SECURITY/);
 assert.doesNotMatch(sql,/CREATE TRIGGER|INSERT INTO club\.app_private_messages|INSERT INTO club\.app_chat_messages|SET status='sent'|SET status='published'/);
});
test('PostgreSQL independently verifies live canonical coach-player assignment',()=>{
 assert.match(sql,/WHERE i\.id=_identity AND i\.normalized_phone=_phone/);
 assert.match(sql,/i\.role='coach' AND i\.is_active/);
 assert.match(sql,/l\.collection='staff' AND l\.kind='staff_own'/);
 assert.match(sql,/identity\.canonical_contact_bindings/);
 assert.match(sql,/s\.is_active AND NOT s\.is_archived/);
 assert.match(sql,/v_player\.team_type='First Team'/);
 assert.match(sql,/club\.staff_teams/);
 assert.match(sql,/v_player\.team_type='Academy'/);
 assert.match(sql,/club\.staff_academy_groups/);
 assert.match(sql,/club\.academy_group_coaches/);
 assert.match(sql,/club\.player_academy_groups/);
 assert.match(sql,/v_player\.organization_id|organization_id=_owner/);
 assert.match(sql,/migration\.staff_relationship_import_state/);
 assert.match(sql,/SECURITY DEFINER/);
 assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
 assert.match(sql,/INSERT INTO audit\.security_events/);
});
test('API never takes coach identity or name from JSON body',()=>{
 assert.match(endpoints,/MapPost\("\/api\/mobile\/coach\/feedback\/drafts"/);
 assert.match(endpoints,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(endpoints,/MobilePrincipal\.Read\(context\.User\)/);
 assert.match(store,/actor\.Role != MobileRole\.Coach/);
 assert.match(store,/SELECT club\.create_mobile_coach_feedback_draft/);
 assert.match(store,/dto\.RequiresCoachApproval/);
 assert.match(program,/AddScoped<PostgresCoachFeedbackDraftStore>/);
 assert.match(program,/MapMobileCoachFeedback\(\)/);
 assert.doesNotMatch(endpoints,/CoachId|CoachName|SenderId|SendAsync|PublishAsync|GenerateAsync/);
});
test('only function execute granted and no direct draft table access',()=>{
 const sig='club.create_mobile_coach_feedback_draft(\n  uuid,text,text,timestamptz,uuid,text,text,text,text,text)';
 assert.ok(grants.includes('REVOKE ALL ON TABLE club.coach_feedback_drafts FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"app_role"'));
 assert.ok(perms.includes("row_security_active('club.coach_feedback_drafts')"));
 assert.ok(perms.includes("'club.create_mobile_coach_feedback_draft(uuid,text,text,timestamptz,uuid,text,text,text,text,text)'"));
});
