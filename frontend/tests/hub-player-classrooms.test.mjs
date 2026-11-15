import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {classroomTopics,validPlayerClassroomPage} from '../src/lib/mobile/player-classrooms.ts';
import {bearerRouteGroups,reviewedHiddenMobileEntries} from '../../deploy/preview/generate-edge.mjs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/Hub/MobilePlayerWeeklyQuizEndpoints.cs');
const store=read('backend/src/LefteriaFc.Api/Hub/MobilePlayerClassroomStore.cs');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115999000_HubPlayerClassroomProgress.cs');
const screen=read('frontend/src/lib/mobile/components/MobilePlayerClassrooms.svelte');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const grants=read('ops/postgres/identity-app-grants.sql');
const permissions=read('ops/postgres/identity-app-permissions-check.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const integration=read('ops/postgres/hub-player-classroom-checks.sql');
const runner=read('scripts/workspace/run-integration-tests.sh');

const sample=()=>({
  organization_id:'lefteria_fc_academy',
  audience:'player',verified_position:'defender',
  lessons:{approved:3,completed:1,next_title:'Defensive positioning',next_kind:'course'},
  quizzes:{started:true,completed_weeks:1,current_week:2,next_quiz_title:null,next_quiz_topic:null},
  subjects:classroomTopics.map(topic=>({
    topic,published_quizzes:1,completed_quizzes:0,
    correct_answers:0,total_answers:0
  }))
});

test('classroom DTO rejects cross-owner, parent, inflated score, duplicate skill and forged progress',()=>{
  assert.equal(classroomTopics.length,11);
  assert.equal(validPlayerClassroomPage(sample(),'lefteria_fc_academy'),true);
  assert.equal(validPlayerClassroomPage(sample(),'lefteria_fc'),false);
  for(const mutate of [
    x=>x.audience='parent',
    x=>x.verified_position='arbitrary',
    x=>x.subjects[0].completed_quizzes=2,
    x=>x.subjects[0].correct_answers=5,
    x=>x.subjects[0].total_answers=4,
    x=>x.subjects[1].topic='defending',
    x=>x.lessons.completed=4,
    x=>x.lessons.next_kind='video',
    x=>x.quizzes.current_week=3,
    x=>x.quizzes.next_quiz_title='Hidden answer',
    x=>{x.quizzes.started=false;x.quizzes.completed_weeks=1;}
  ]){
    const altered=sample();
    mutate(altered);
    assert.equal(validPlayerClassroomPage(altered,'lefteria_fc_academy'),false);
  }
});

test('private endpoint is exact-path Mobile JWT and owner guarded; preview allows only GET',()=>{
  assert.match(api,/MapGet\("\/api\/mobile\/hub\/player-classrooms"/);
  assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
  assert.match(api,/ExcludeFromDescription\(\)/);
  assert.match(api,/Organization\(organization_id,context\)/);
  assert.match(store,/actor\.Role!=MobileRole\.Player/);
  assert.match(store,/hub\.read_mobile_player_classrooms/);
  assert.match(store,/result\.OrganizationId!=organization/);
  assert.match(controller,/getPlayerClassrooms\(/);
  assert.match(controller,/state\.user\?\.role!=='player'/);
  assert.match(controller,/revision!==actorRevision/);
  assert.match(controller,/validPlayerClassroomPage\(result,organization\)/);
  assert.deepEqual(reviewedHiddenMobileEntries,[['get','/api/mobile/hub/player-classrooms']]);
  const contract=JSON.parse(read('contracts/openapi.json'));
  const routes=bearerRouteGroups(contract);
  const permitted=(method,path)=>routes.some(x=>
    x.method===method&&new RegExp(x.expression).test(path));
  assert.equal(permitted('GET','/api/mobile/hub/player-classrooms'),true);
  assert.equal(permitted('POST','/api/mobile/hub/player-classrooms'),false);
  assert.equal(permitted('GET','/api/mobile/hub/player-classrooms/other'),false);
  assert.equal(permitted('GET','/api/mobile/hub/classrooms'),false);
});

test('PostgreSQL never authorizes classroom through role hint, parent or other organization',()=>{
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/hub\.assert_current_quiz_player\(/);
  assert.match(migration,/hub\.current_mobile_education_lesson\(/);
  assert.match(migration,/hub\.read_mobile_player_weekly_quiz\(/);
  assert.match(migration,/q\.reviewed_by IS NOT NULL/);
  assert.match(migration,/q\.status='published'/);
  assert.match(migration,/c\.age_min IS NULL AND c\.age_max IS NULL/);
  assert.match(migration,/done\.identity_id=_identity/);
  assert.match(migration,/a\.identity_id=_identity/);
  assert.match(migration,/status='published'/);
  assert.match(migration,/REVOKE ALL ON FUNCTION hub\.read_mobile_player_classrooms/);
  assert.doesNotMatch(migration,/INSERT INTO hub\.|UPDATE hub\.|DELETE FROM hub\./);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.read_mobile_player_classrooms/);
  assert.match(permissions,/hub\.read_mobile_player_classrooms\(uuid,text,text,timestamptz,text\)/);
});

test('classroom integrates with approved lesson and quiz destinations without fake content or local persistence',()=>{
  assert.ok(compile(screen,{filename:'MobilePlayerClassrooms.svelte',generate:'server'}).js.code.length>500);
  assert.match(route,/MobilePlayerClassrooms/);
  assert.match(route,/id="hub-player-lesson"/);
  assert.match(route,/id="hub-player-quiz"/);
  assert.match(screen,/href="#hub-player-lesson"/);
  assert.match(screen,/href="#hub-player-quiz"/);
  assert.match(screen,/data-testid="hub-classroom-subjects"/);
  assert.match(screen,/page\.subjects as subject \(subject\.topic\)/);
  assert.match(screen,/controller\.getPlayerClassrooms\(owner\)/);
  assert.match(screen,/page=null;error=null;busy=true/);
  assert.match(screen,/aria-pressed=\{organization==='lefteria_fc_academy'\}/);
  assert.match(screen,/aria-pressed=\{organization==='lefteria_fc'\}/);
  assert.doesNotMatch(screen,/localStorage|sessionStorage|innerHTML|\{@html|correct_index/);
});

test('real disposable Postgres covers Week 1, private marks, withdrawal, denied readers',()=>{
  assert.match(integration,/BEGIN;[\s\S]*ROLLBACK;/);
  for(const marker of [
    'player_classroom_first_week_projection_failed',
    'player_classroom_week_one_quiz_not_ready',
    'player_classroom_individual_progress_not_recorded',
    'withdrawn_quiz_was_advertised_or_history_deleted',
    'parent_read_player_classroom','unlinked_player_read_classroom',
    'first_team_academy_classroom_leak','expired_session_read_classroom',
    'forged_phone_read_classroom','deactivated_player_read_classroom'
  ])assert.ok(integration.includes(marker),marker);
  assert.match(workflow,/hub-player-classroom-checks\.sql/);
  assert.match(runner,/hub-player-classroom-checks\.sql/);
});
