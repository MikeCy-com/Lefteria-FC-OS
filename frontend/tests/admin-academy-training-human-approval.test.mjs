import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {
  validTrainingPlanApprovals,validTrainingPlanApprovalResult
} from '../src/lib/admin/academy-training-plans.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const panel=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const migration=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113100000_AcademyTrainingHumanApproval.cs');
const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
const sql=read('../../ops/postgres/academy-training-plans-lifecycle-checks.sql');
const grants=read('../../ops/postgres/academy-training-plans-admin-grants.sql');
const plan={
  id:'f65136f2-a7af-42f9-9ec2-d233531086ae',
  organization_id:'lefteria_fc_academy',group_id:'u12',
  day:'2026-11-20',start_time:'17:00',duration_minutes:60,
  title:'Training',location:'Pitch',notes:'',
  status:'draft',version:2
};
const page={organization_id:'lefteria_fc_academy',group_id:'u12',
  from:'2026-11-01',to:'2026-11-30',published:false,
  approvals:[{id:plan.id,approved_version:2}]};
test('only currently approved exact version is displayed in manager plan list',()=>{
  assert.equal(validTrainingPlanApprovals(page,'u12',page.from,page.to,[plan]),true);
  assert.equal(validTrainingPlanApprovals({...page,published:true},'u12',page.from,page.to,[plan]),false);
  assert.equal(validTrainingPlanApprovals({...page,organization_id:'lefteria_fc'},'u12',page.from,page.to,[plan]),false);
  assert.equal(validTrainingPlanApprovals({...page,approvals:[{id:plan.id,approved_version:1}]},'u12',page.from,page.to,[plan]),false);
  assert.equal(validTrainingPlanApprovals({...page,approvals:[...page.approvals,...page.approvals]},'u12',page.from,page.to,[plan]),false);
  assert.equal(validTrainingPlanApprovals({...page,approvals:[{id:'foreign',approved_version:2}]},'u12',page.from,page.to,[plan]),false);
  assert.equal(validTrainingPlanApprovals(page,'u12',page.from,page.to,[{...plan,status:'archived'}]),false);
  assert.equal(validTrainingPlanApprovalResult({
    id:plan.id,organization_id:'lefteria_fc_academy',
    approval_status:'approved',approved_version:2,published:false
  },plan),true);
  assert.equal(validTrainingPlanApprovalResult({
    id:plan.id,organization_id:'lefteria_fc_academy',
    approval_status:'approved',approved_version:1,published:false
  },plan),false);
});
test('human approval UI is manual, separately validated and never claims publishing',()=>{
  assert.ok(compile(panel,{filename:'AdminAcademyTrainingPlans.svelte',generate:'server'}).js.code.length>0);
  assert.match(panel,/reviewConfirmed/);
  assert.match(panel,/validTrainingPlanApprovals\(approvals,groupId,from,to,raw\.plans\)/);
  assert.match(panel,/confirm_human_review:true/);
  assert.match(panel,/expected_version:plan\.version/);
  assert.match(panel,/validTrainingPlanApprovalResult\(raw,plan\)/);
  assert.match(panel,/ΔΕΝ δημοσιεύτηκε/);
  assert.doesNotMatch(panel,/publishTraining|markPublished|localStorage/);
});
test('approval stores immutable signed version and reauthorizes in SQL',()=>{
  assert.match(migration,/identity\.admin_accounts/);
  assert.match(migration,/plan_version integer NOT NULL/);
  assert.match(migration,/UNIQUE\(plan_id,plan_version\)/);
  assert.match(migration,/club\.require_academy_training_planner\(_digest,_owner\)/);
  assert.match(migration,/FOR UPDATE/);
  assert.match(migration,/v_plan\.version<>_expected/);
  assert.match(migration,/v_plan\.status<>'draft'/);
  assert.match(migration,/ENABLE ROW LEVEL SECURITY/);
  assert.match(migration,/published',false/);
  assert.match(api,/MapPost\("\/{planId:guid}\/approve"/);
  assert.match(api,/ConfirmHumanReview/);
  assert.match(api,/training:write/);
  assert.match(sql,/Editing an approved draft preserved stale approval/);
  assert.match(sql,/Revoked manager could approve a training plan/);
  assert.match(grants,/club\.approve_admin_academy_training_plan/);
  assert.match(grants,/club\.academy_training_plan_approvals'::regclass/);
  assert.doesNotMatch(migration,/INSERT INTO club\.calendar_items|UPDATE club\.calendar_items|INSERT INTO club\.calendar_responses/);
});
