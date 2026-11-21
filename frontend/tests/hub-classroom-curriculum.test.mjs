import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {classroomTopics,validPlayerClassroomPage} from '../src/lib/mobile/player-classrooms.ts';
import {entryRouteGroups,reviewedHiddenAdminEntries} from '../../deploy/preview/generate-edge.mjs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261121160000_HubClassroomLessonSubjectMapping.cs');
const backend=read('backend/src/LefteriaFc.Api/Hub/AdminClassroomCurriculumEndpoints.cs');
const mobile=read('backend/src/LefteriaFc.Api/Hub/MobilePlayerClassroomStore.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const root=read('frontend/src/routes/admin/hub/+page.svelte');
const editor=read('frontend/src/lib/admin/AdminClassroomCurriculum.svelte');
const classroom=read('frontend/src/lib/mobile/components/MobilePlayerClassrooms.svelte');
const sql=read('ops/postgres/hub-classroom-curriculum-checks.sql');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const checks=read('ops/postgres/web-admin-auth-permissions-check.sql');
const ci=read('.github/workflows/rewrite-build.yml');
const runner=read('scripts/workspace/run-integration-tests.sh');
const uuid='11111111-1111-4111-8111-111111111111';

test('lesson classification is an audited, supervised, revision-bound overlay',()=>{
  for(const marker of [
    'CREATE TABLE hub.classroom_lesson_subjects',
    'CREATE TABLE hub.classroom_lesson_subject_events',
    'hub.assert_admin_learning_position_authority(_digest,_owner)',
    "c.kind IN ('article','course')",
    "c.audience_player AND c.status='published'",
    'c.age_min IS NULL AND c.age_max IS NULL',
    "rev.text_review='passed' AND rev.visual_review='passed'",
    'v_latest IS DISTINCT FROM _revision',
    '_expected_version<>v_version',
    'INSERT INTO hub.classroom_lesson_subject_events',
    'INSERT INTO audit.security_events',
    'CREATE OR REPLACE FUNCTION hub.read_mobile_player_classrooms',
    'map.revision_id',
    'map.organization_id=_owner',
    'done.identity_id=_identity',
    "'approved_lessons'",
    "'completed_lessons'"
  ])assert.ok(migration.includes(marker),marker);
  assert.doesNotMatch(migration,/CREATE OR REPLACE FUNCTION hub\.current_mobile_education_lesson/);
  assert.doesNotMatch(migration,/CREATE OR REPLACE FUNCTION hub\.complete_mobile_next_education/);
  assert.doesNotMatch(migration,/INSERT INTO hub\.education_completions/);
});

test('Super Admin-only, owner-scoped endpoints have exact audited preview rules',()=>{
  for(const marker of [
    'MapGroup("/api/admin/hub/classroom-curriculum")',
    '.RequireAuthorization(WebAdminServices.Policy)',
    '.ExcludeFromDescription()',
    'group.MapGet("',
    'group.MapPut("/{contentId:guid}"',
    'session.User.Role!="super_admin"',
    'context.Request.Query["organization_id"].Count!=1',
    'session.User.Organizations.Any(o=>o.Id==owner)',
    'payload.ExpectedVersion'
  ])assert.ok(backend.includes(marker),marker);
  assert.match(program,/app\.MapAdminClassroomCurriculum\(\)/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.read_admin_classroom_curriculum/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.set_admin_classroom_lesson_subject/);
  assert.match(grants,/REVOKE ALL ON TABLE hub\.classroom_lesson_subjects/);
  assert.match(checks,/hub\.set_admin_classroom_lesson_subject\(text,text,uuid,uuid,bigint,text,text\)/);
  assert.deepEqual(reviewedHiddenAdminEntries.filter(([,p])=>p.includes('classroom-curriculum')),[
    ['get','/api/admin/hub/classroom-curriculum'],
    ['put','/api/admin/hub/classroom-curriculum/{contentId}']
  ]);
  const contract=JSON.parse(read('contracts/openapi.json'));
  const rules=entryRouteGroups(contract);
  const allowed=(method,path)=>rules.some(x=>x.method===method&&
    new RegExp(x.expression).test(path));
  assert.equal(allowed('GET','/api/admin/hub/classroom-curriculum'),true);
  assert.equal(allowed('PUT','/api/admin/hub/classroom-curriculum/'+uuid),true);
  assert.equal(allowed('POST','/api/admin/hub/classroom-curriculum/'+uuid),false);
  assert.equal(allowed('GET','/api/admin/hub/classroom-curriculum/extra'),false);
});

test('private classrooms and Super Admin editor compile, reject forged subject totals',()=>{
  assert.ok(compile(editor,{filename:'AdminClassroomCurriculum.svelte',generate:'server'}).js.code.length>1000);
  assert.ok(compile(classroom,{filename:'MobilePlayerClassrooms.svelte',generate:'server'}).js.code.length>1000);
  assert.match(root,/\$auth\.session\.user\.role==='super_admin'/);
  assert.match(root,/<AdminClassroomCurriculum \{organization\}/);
  assert.match(editor,/expected_version:selectedLesson\.version/);
  assert.match(editor,/revision_id:selectedLesson\.revision_id/);
  assert.match(editor,/onExpired\(\)/);
  assert.match(editor,/onRevoked\(\)/);
  assert.match(editor,/data-testid="hub-classroom-curriculum-admin"/);
  assert.match(editor,/needs_reverification/);
  assert.doesNotMatch(editor,/localStorage|sessionStorage|\{@html|innerHTML/);
  assert.match(classroom,/data-testid="hub-classroom-lesson-subject"/);
  assert.match(classroom,/subject\.completed_lessons/);
  assert.match(mobile,/item\.CompletedLessons>item\.ApprovedLessons/);
  const datum={
    organization_id:'lefteria_fc_academy',audience:'player',verified_position:'defender',
    lessons:{approved:2,completed:1,next_title:'Safe next',next_kind:'course'},
    quizzes:{started:false,completed_weeks:0,current_week:1,next_quiz_title:null,next_quiz_topic:null},
    subjects:classroomTopics.map(topic=>({
      topic,published_quizzes:0,completed_quizzes:0,
      correct_answers:0,total_answers:0,approved_lessons:0,completed_lessons:0
    }))
  };
  datum.subjects[0].approved_lessons=2;
  datum.subjects[0].completed_lessons=1;
  assert.equal(validPlayerClassroomPage(datum,'lefteria_fc_academy'),true);
  datum.subjects[0].completed_lessons=3;
  assert.equal(validPlayerClassroomPage(datum,'lefteria_fc_academy'),false);
});

test('rollback-only PostgreSQL checks revisions, withdrawal, role denial and personal marks',()=>{
  assert.match(sql,/BEGIN;[\s\S]*ROLLBACK;/);
  for(const marker of [
    'curriculum_private_admin_list_leaked',
    'manager_classroom_curriculum_access',
    'foreign_super_admin_curriculum_write',
    'parent_only_lesson_mapped_to_player',
    'classroom_subject_pre_completion_wrong',
    'classroom_subject_personal_completion_missing',
    'curriculum_stale_version_accepted',
    'stale_revision_advertised_in_classroom',
    'stale_revision_not_flagged_for_admin',
    'stale_classroom_revision_accepted',
    'classroom_revision_reverification_failed',
    'classroom_immutable_event_history_lost',
    'revised_classroom_completion_wrong',
    'withdrawn_classroom_content_advertised',
    'withdrawn_classroom_content_classified',
    'revoked_curriculum_admin_read'
  ])assert.ok(sql.includes(marker),marker);
  assert.match(ci,/hub-classroom-curriculum-checks\.sql/);
  assert.match(runner,/hub-classroom-curriculum-checks\.sql/);
});
