import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {entryRouteGroups,reviewedHiddenAdminEntries} from '../../deploy/preview/generate-edge.mjs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261121150000_HubVerifiedPlayerLearningPositions.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Hub/AdminPlayerLearningPositionEndpoints.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const editor=read('frontend/src/routes/admin/hub/player-quizzes/+page.svelte');
const picker=read('frontend/src/lib/admin/AdminVerifiedLearningPosition.svelte');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const adminCheck=read('ops/postgres/web-admin-auth-permissions-check.sql');
const mobileCheck=read('ops/postgres/identity-app-permissions-check.sql');
const sql=read('ops/postgres/hub-player-learning-position-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const runner=read('scripts/workspace/run-integration-tests.sh');

test('position specialization is a reviewed, versioned, audited overlay; canonical roster stays intact',()=>{
  assert.match(migration,/CREATE TABLE hub\.player_learning_specializations/);
  assert.match(migration,/CREATE TABLE hub\.player_learning_specialization_events/);
  assert.match(migration,/FOREIGN KEY\(organization_id,player_id\)/);
  assert.match(migration,/REFERENCES club\.players\(organization_id,id\)/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION hub\.assert_current_quiz_player/);
  assert.match(migration,/JOIN club\.players p ON p\.id=l\.record_id/);
  assert.match(migration,/i\.normalized_phone=_phone/);
  assert.match(migration,/s\.roster_position=v_position/);
  assert.match(migration,/FOR SHARE OF i,l,b,p/);
  for(const [base,role] of [
    ['Goalkeeper','goalkeeper'],['Defender','defender'],['Midfielder','midfielder']
  ])assert.match(migration,new RegExp("WHEN '"+base+"' THEN '"+role+"'"));
  assert.match(migration,/ELSE 'all' END/);
  assert.match(migration,/v_new:=NULLIF\(_specialization,'all'\)/);
  assert.match(migration,/v_roster='Forward' AND v_new IN \('striker','winger'\)/);
  assert.match(migration,/v_roster='Defender' AND v_new IN \('defender','fullback'\)/);
  assert.match(migration,/v_roster='Midfielder' AND v_new IN \('midfielder','winger'\)/);
  assert.match(migration,/v_roster<>_expected_roster/);
  assert.match(migration,/_expected_version<>v_version/);
  assert.match(migration,/AND a\.role='super_admin'/);
  assert.match(migration,/s\.revoked_at IS NULL/);
  assert.match(migration,/g\.organization_id=_owner/);
  assert.match(migration,/INSERT INTO hub\.player_learning_specialization_events/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.doesNotMatch(migration,/UPDATE club\.players|INSERT INTO club\.players|DELETE FROM club\.players/);
});

test('Super Admin-only, owner-bound API keeps mobile and preview contracts closed',()=>{
  assert.match(endpoint,/MapGroup\("\/api\/admin\/hub\/players\/\{playerId\}\/learning-position"\)/);
  assert.match(endpoint,/\.RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(endpoint,/\.ExcludeFromDescription\(\)/);
  assert.match(endpoint,/group\.MapGet\(""/);
  assert.match(endpoint,/group\.MapPut\(""/);
  assert.match(endpoint,/session\.User\.Role!="super_admin"/);
  assert.match(endpoint,/context\.Request\.Query\["organization_id"\]\.Count!=1/);
  assert.match(endpoint,/session\.User\.Organizations\.Any\(o=>o\.Id==organization\)/);
  assert.match(endpoint,/body\.ExpectedVersion/);
  assert.match(endpoint,/ex\.SqlState switch/);
  assert.match(program,/app\.MapAdminPlayerLearningPositions\(\)/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.read_admin_player_learning_position/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.set_admin_player_learning_position/);
  assert.match(grants,/REVOKE ALL ON FUNCTION hub\.assert_admin_learning_position_authority/);
  assert.match(grants,/REVOKE ALL ON TABLE hub\.player_learning_specializations/);
  assert.match(adminCheck,/hub\.read_admin_player_learning_position\(text,text,text\)/);
  assert.match(adminCheck,/hub\.set_admin_player_learning_position\(text,text,text,text,bigint,text,text\)/);
  assert.doesNotMatch(mobileCheck,/GRANT EXECUTE ON FUNCTION hub\.set_admin_player_learning_position/);

  const operations=reviewedHiddenAdminEntries.filter(
    ([,path])=>path==='/api/admin/hub/players/{playerId}/learning-position');
  assert.deepEqual(operations,[
    ['get','/api/admin/hub/players/{playerId}/learning-position'],
    ['put','/api/admin/hub/players/{playerId}/learning-position']
  ]);
  const contract=JSON.parse(read('contracts/openapi.json'));
  const routing=entryRouteGroups(contract);
  const allowed=(method,path)=>routing.some(
    rule=>rule.method===method&&new RegExp(rule.expression).test(path));
  assert.equal(allowed('GET','/api/admin/hub/players/valid-player/learning-position'),true);
  assert.equal(allowed('PUT','/api/admin/hub/players/valid-player/learning-position'),true);
  assert.equal(allowed('POST','/api/admin/hub/players/valid-player/learning-position'),false);
  assert.equal(allowed('PUT','/api/admin/hub/players/valid-player/learning-position/extra'),false);
});

test('Super Admin editor uses selected authoritative roster, not a client-chosen player role',()=>{
  assert.ok(compile(picker,{filename:'AdminVerifiedLearningPosition.svelte',generate:'server'}).js.code.length>250);
  assert.match(editor,/\$auth\.session\.user\.role==='super_admin'/);
  assert.match(editor,/<AdminVerifiedLearningPosition \{organization\} actorId=\{\$auth\.session\.user\.id\}\/>/);
  assert.match(picker,/\/api\/admin\/football\/players\?/);
  assert.match(picker,/limit:'20'/);
  assert.match(picker,/data-testid="hub-verified-player-specialization"/);
  assert.match(picker,/expected_roster_position:details\.roster_position/);
  assert.match(picker,/expected_version:details\.version/);
  assert.match(picker,/method:'PUT'/);
  assert.match(picker,/X-Lefteria-Admin/);
  assert.match(picker,/response\.status===409/);
  assert.match(picker,/needs_reverification/);
  assert.match(picker,/reason\.length<8/);
  assert.doesNotMatch(picker,/localStorage|sessionStorage|innerHTML|\{@html/);
});

test('real PostgreSQL regression tests cover active Super Admin, position accuracy and stale revocation',()=>{
  assert.match(sql,/BEGIN;[\s\S]*ROLLBACK;/);
  for(const name of [
    'unverified_forward_was_guessed',
    'manager_approved_player_specialization',
    'ungranted_super_admin_read_specialization',
    'cross_club_specialization_applied',
    'verified_winger_not_selected_by_player',
    'verified_winger_week_one_quiz_invalid',
    'verified_fullback_not_selected',
    'incompatible_defender_striker_accepted',
    'stale_specialization_version_accepted',
    'stale_specialization_survived_roster_change',
    'roster_change_not_flagged_for_reverification',
    'stale_canonical_roster_write_accepted',
    'specialty_revocation_did_not_fallback',
    'audit_history_lost_on_revoke',
    'revoked_admin_session_read_specialization'
  ])assert.ok(sql.includes(name),name);
  assert.match(workflow,/hub-player-learning-position-checks\.sql/);
  assert.match(runner,/hub-player-learning-position-checks\.sql/);
});
