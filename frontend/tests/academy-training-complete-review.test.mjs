import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validMemberTrainingSummary,matchingCompleteTrainingReview} from '../src/lib/admin/academy-training-drills.ts';
import {validTrainingDistributionPreview} from '../src/lib/admin/academy-training-plans.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const plan={id:'e76aa6ed-96c5-4a7c-8df4-d44ef2809811',organization_id:'lefteria_fc_academy',
  group_id:'ci-group',version:5,status:'draft',duration_minutes:60,day:'2026-12-01',start_time:'17:00',
  title:'Training',location:'Pitch',notes:'Private',drill_count:2};
const summary='Passing and warm-up\nBring water';
const approval={id:plan.id,organization_id:plan.organization_id,group_id:plan.group_id,approved_version:5,
  approval_status:'approved',published:false,member_summary:summary};
const preview={id:plan.id,organization_id:plan.organization_id,group_id:plan.group_id,
  version:plan.version,day:plan.day,start_time:plan.start_time,duration_minutes:plan.duration_minutes,
  title:plan.title,location:plan.location,eligible_player_count:12,human_approved:true,published:false,member_summary:summary};
test('member summary has a bounded plain-text contract and exact complete-review response',()=>{
  assert.equal(validMemberTrainingSummary(summary),true);
  for(const value of ['', ' ', ' leading', 'trailing ', 'bad\u0000text','bad\ttext','x'.repeat(1201),null,12])
    assert.equal(validMemberTrainingSummary(value),false);
  assert.equal(matchingCompleteTrainingReview(approval,plan,summary),true);
  for(const bad of [{...approval,approved_version:4},{...approval,group_id:'other'},{...approval,published:true},
    {...approval,member_summary:'changed'},{...approval,drill_snapshots:[]},{...approval,private_notes:'secret'}])
    assert.equal(matchingCompleteTrainingReview(bad,plan,summary),false);
});
test('recipient preview allows the reviewed summary but excludes all private data',()=>{
  assert.equal(validTrainingDistributionPreview(preview,plan),true);
  for(const bad of [{...preview,version:4},{...preview,group_id:'other'},{...preview,notes:'secret'},
    {...preview,drill_snapshots:[]},{...preview,member_summary:'x'.repeat(1201)},{...preview,member_summary:null}])
    assert.equal(validTrainingDistributionPreview(bad,plan),false);
  const {member_summary,...legacy}=preview;
  assert.equal(validTrainingDistributionPreview(legacy,plan),true); // schedule-only approval does not distribute coaching content
  assert.equal(validTrainingDistributionPreview(legacy,{...plan,drill_count:0}),true);
  assert.equal(validTrainingDistributionPreview(preview,{...plan,drill_count:0}),false);
});
test('complete review reauthorizes group and separates coaching snapshots from member projection',()=>{
  const sql=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125092000_AcademyTrainingCompleteReview.cs');
  const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
  const lifecycle=read('../../ops/postgres/academy-training-drill-builder-checks.sql');
  assert.match(sql,/NEW.plan_snapshot->>'review_kind' IS DISTINCT FROM 'complete_drill_plan'/);
  assert.match(sql,/NEW.plan_snapshot->'drill_snapshots' IS DISTINCT FROM v_drills/);
  assert.match(sql,/club.require_training_drill_group\(v_actor,_group\)/);
  assert.match(sql,/AND group_id=_group FOR UPDATE/);
  assert.match(sql,/a.plan_snapshot->'drill_snapshots'=v_plan.drill_snapshots/);
  assert.match(sql,/'summary',v_member_summary,'journey',NULL/);
  assert.match(api,/MapPost\("\/{planId:guid}\/drills\/approve"/);
  const projection=sql.slice(sql.indexOf('v_projection:=jsonb_build_object('),sql.indexOf('INSERT INTO migration.calendar_source_documents'));
  assert.doesNotMatch(projection,/drill_snapshots|instructions|v_plan.notes/);
  for(const marker of ['foreign_complete_review_accepted','unconfirmed_complete_review_accepted','stale_complete_review_accepted',
    'foreign_complete_preview_accepted','foreign_complete_publication_accepted','unsafe_complete_publication','native_member_scope_missing','published_sequence_edit_accepted','foreign_complete_cancel_accepted'])
    assert.ok(lifecycle.includes(marker));
});
test('review UI separates approval and publication and pins confirmation to plan revision',()=>{
  const ui=read('../src/lib/admin/AdminTrainingDrillBuilder.svelte'),workspace=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
  for(const [filename,source] of [['AdminTrainingDrillBuilder.svelte',ui],['AdminAcademyTrainingPlans.svelte',workspace]])
    for(const generate of ['client','server'])compile(source,{filename,generate});
  assert.match(ui,/canEdit=\$derived\(canWrite&&!reviewMode\)/);
  assert.match(ui,/matchingCompleteTrainingReview\(value,plan,summary\)/);
  assert.match(ui,/oninput=\{\(\)=>reviewConfirmed=false\}/);
  assert.match(workspace,/reviewConfirmed!==reviewKey\(plan\)/);
  assert.match(workspace,/preview.member_summary/);
  assert.match(workspace,/async function inspect\(plan:TrainingPlanDraft\)\{\s*if\(!browser\|\|!allowed\|\|coachMode\|\|drillTarget\|\|typeof plan.drill_count!=='number'/);
});
