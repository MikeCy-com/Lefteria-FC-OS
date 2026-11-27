import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validTrainingSubmission} from '../src/lib/admin/academy-training-plans.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const sql=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125100000_AcademyAssignedCoachWorkspace.cs');
const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
const planUI=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const schedule=read('../src/routes/admin/schedule/+page.svelte');
const attendance=read('../src/routes/admin/attendance/+page.svelte');
const nav=read('../src/lib/admin/AdminFrame.svelte');
const plan={id:'e76aa6ed-96c5-4a7c-8df4-d44ef2809811',organization_id:'lefteria_fc_academy',group_id:'ci-group',version:3};
const response={id:plan.id,organization_id:plan.organization_id,group_id:plan.group_id,submitted_version:3,submitted:true,published:false};
test('submission response requires exact version and group and cannot claim approval or publication',()=>{
  assert.equal(validTrainingSubmission(response,plan),true);
  for(const bad of [{...response,submitted_version:2},{...response,group_id:'foreign'},{...response,published:true},
    {...response,approved:true},{...response,submitted:false},{...response,organization_id:'lefteria_fc'}])
    assert.equal(validTrainingSubmission(bad,plan),false);
});
test('canonical staff assignment, active location and locked authorization apply to assigned operations',()=>{
  assert.match(sql,/FROM identity.admin_staff_links link JOIN club.staff st ON st.id=link.staff_id/);
  assert.match(sql,/FOR SHARE OF link,st/);
  assert.match(sql,/AND g.academy_group_id=_group FOR SHARE/);
  assert.match(sql,/club.academy_group_coaches g/);
  assert.match(sql,/c.id=a.city_id AND c.is_active/);
  assert.match(sql,/l.city_id=a.city_id AND l.is_active/);
  assert.match(sql,/club.require_training_draft_actor\(_digest,_owner,_group\)/);
  assert.match(sql,/training_coach_reviewed_plan_locked/);
  assert.match(sql,/PRIMARY KEY\(plan_id,plan_version\)/);
  assert.match(sql,/v_role IS DISTINCT FROM 'coach'/);
  assert.match(sql,/sub.plan_version=p.version/);
  assert.match(sql,/academy_training_plan_submissions ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(sql,/CREATE OR REPLACE FUNCTION club\.(approve_admin_academy_training_plan|approve_admin_training_drill_plan|publish_admin_academy_training_plan|cancel_admin_academy_training_publication)\(/);
});
test('manager-only routes never opt into coach authorization',()=>{
  assert.match(api,/bool allowAssigned=false/);
  assert.match(api,/write\?"training:write_assigned":"training:read_assigned"/);
  for(const route of ['/approve','/drills/approve','/publish','/cancel','/publication-preview']){
    const start=api.indexOf('("/{planId:guid}'+route+'"');
    assert.ok(start>=0,route);
    const part=api.slice(start,api.indexOf('var cookie=await Authorized',start)+110);
    assert.doesNotMatch(part,/allowAssigned:true/);
  }
  assert.match(api,/MapPost\("\/\{planId:guid\}\/submit"/);
  assert.match(api,/MapGet\("\/assigned-groups"/);
});
test('coach navigation and exact-version submit UI use assigned group endpoint',()=>{
  assert.match(schedule,/coach\?'\/api\/admin\/academy\/training\/plans\/assigned-groups'/);
  assert.match(attendance,/coach\?'\/api\/admin\/academy\/training\/plans\/assigned-groups'/);
  assert.match(planUI,/const canManage=\$derived\(canWrite&&!coachMode\)/);
  assert.match(planUI,/submitTarget!==reviewKey\(plan\)/);
  assert.match(planUI,/confirm_submission:true/);
  assert.match(planUI,/validTrainingSubmission\(raw,plan\)/);
  assert.match(nav,/data-testid="admin-coach-workspace-link"/);
});
test('database fixtures cover unauthorized groups, stale submission, manager review and revoked attendance',()=>{
  const draft=read('../../ops/postgres/academy-training-drill-builder-checks.sql');
  const marks=read('../../ops/postgres/academy-attendance-lifecycle-checks.sql');
  for(const marker of ['coach_foreign_group_read_accepted','coach_foreign_group_write_accepted','coach_self_approval_accepted',
    'coach_publish_accepted','manager_submission_missing','stale_coach_submission_visible','coach_approved_edit_accepted','revoked_coach_assignment_accepted'])
    assert.ok(draft.includes(marker),marker);
  for(const marker of ['coach_attendance_roster_missing','coach_outsider_attendance_accepted','coach_attendance_write_failed',
    'coach_removed_attendance_read_accepted','coach_removed_attendance_write_accepted'])
    assert.ok(marks.includes(marker),marker);
});
test('workspace components compile for browser and server',()=>{
  for(const [filename,source] of [['AdminAcademyTrainingPlans.svelte',planUI],['schedule/+page.svelte',schedule],['attendance/+page.svelte',attendance],['AdminFrame.svelte',nav]])
    for(const generate of ['client','server'])compile(source,{filename,generate});
});
