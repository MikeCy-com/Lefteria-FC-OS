import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112170000_CoachFeedbackTargetDirectory.cs');
const endpoints=read('backend/src/LefteriaFc.Api/Messaging/MobileCoachFeedbackEndpoints.cs');
const store=read('backend/src/LefteriaFc.Infrastructure/Messaging/PostgresCoachFeedbackDraftStore.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const checks=read('ops/postgres/identity-app-permissions-check.sql');

test('coach feedback target picker is restricted to current canonical coach and assigned player groups',()=>{
 assert.match(migration,/CREATE FUNCTION club\.read_mobile_coach_feedback_targets/);
 assert.match(migration,/_role IS DISTINCT FROM 'coach'/);
 assert.match(migration,/i\.role='coach' AND i\.is_active/);
 assert.match(migration,/l\.collection='staff' AND l\.kind='staff_own'/);
 assert.match(migration,/identity\.canonical_contact_bindings/);
 assert.match(migration,/s\.is_active AND NOT s\.is_archived/);
 assert.match(migration,/p\.is_active AND NOT p\.is_archived AND NOT p\.is_transferred/);
 assert.match(migration,/st\.staff_id=v_staff/);
 assert.match(migration,/st\.team_id=p\.team_id/);
 assert.match(migration,/club\.staff_academy_groups sg/);
 assert.match(migration,/club\.academy_group_coaches gc/);
 assert.match(migration,/club\.player_academy_groups pg/);
 assert.match(migration,/LIMIT 151/);
 assert.match(migration,/v_count>150/);
 assert.match(migration,/jsonb_build_object\(\s*'id',id,'organization_id',organization_id,'name',name\)/);
 assert.doesNotMatch(migration,/phone.*jsonb_build_object|guardian_phone|birth_date|evaluation_score|final_text|INSERT INTO club\.coach_feedback_drafts/);
});
test('mobile picker does not send feedback and uses fresh mobile identity',()=>{
 assert.match(endpoints,/MapGet\("\/api\/mobile\/coach\/feedback\/targets"/);
 assert.match(endpoints,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(endpoints,/MobilePrincipal\.Read\(context\.User\)/);
 assert.match(store,/actor\.Role!=MobileRole\.Coach/);
 assert.match(store,/club\.read_mobile_coach_feedback_targets\(/);
 assert.match(store,/dto\.Items\.Count>150/);
 assert.match(store,/Distinct\(\)\.Count\(\)!=dto\.Items\.Count/);
});
test('raw directory data are not granted; only narrow function receives execute',()=>{
 const sig='club.read_mobile_coach_feedback_targets(';
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig));
 assert.ok(checks.includes("'club.read_mobile_coach_feedback_targets(uuid,text,text,timestamptz)'"));
 assert.match(migration,/SECURITY DEFINER/);
 assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
 assert.match(migration,/REVOKE ALL ON FUNCTION club\.read_mobile_coach_feedback_targets/);
});
