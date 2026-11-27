import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validTrainingDelivery,matchingTrainingDeliverySave,snapshotDrill} from '../src/lib/admin/academy-training-drills.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const sql=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125110000_AcademyCoachDrillAutonomy.cs');
const builder=read('../src/lib/admin/AdminTrainingDrillBuilder.svelte');
const workspace=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const library=read('../src/routes/admin/drills/+page.svelte');
const plan={id:'e76aa6ed-96c5-4a7c-8df4-d44ef2809811',organization_id:'lefteria_fc_academy',group_id:'ci-group',
  version:3,status:'draft',duration_minutes:60};
const drill=snapshotDrill({id:'51ee2557-2727-44f6-a73a-2600a0767a22',version:4,title:'Passing',category:'technical',
  age_band:'all',duration_minutes:20,objective:'Accuracy',instructions:'Pass in pairs'});
const result={...plan,sequence_version:2,drills:[drill]};
test('independent delivery response retains exact schedule and increments only sequence revision',()=>{
  assert.equal(validTrainingDelivery(result,plan),true);
  assert.equal(matchingTrainingDeliverySave(result,plan,1,[drill]),true);
  for(const invalid of [{...result,version:4},{...result,group_id:'foreign'},{...result,sequence_version:1},
    {...result,sequence_version:-1},{...result,member_summary:'private'},{...result,drills:[{...drill,title:'changed'}]}])
    assert.equal(matchingTrainingDeliverySave(invalid,plan,1,[drill]),false);
});
test('delivery writes require exact assigned scope and do not mutate schedule, approvals or publication',()=>{
  const writer=sql.slice(sql.indexOf('CREATE FUNCTION club.write_admin_training_delivery('),sql.indexOf('END $delivery_write$;'));
  assert.match(writer,/club.require_training_draft_actor\(_digest,'lefteria_fc_academy',_group\)/);
  assert.match(writer,/group_id=_group/);
  assert.match(writer,/version<>_schedule/);
  assert.match(writer,/sequence_version'\)::integer<>_expected/);
  assert.match(writer,/status='approved' AND version=v_version FOR SHARE/);
  assert.match(writer,/INSERT INTO club.academy_training_delivery_revisions/);
  assert.match(writer,/academy_training_publication_cancellations/);
  assert.doesNotMatch(writer,/UPDATE club.academy_training_plan_drafts|INSERT INTO club.academy_training_plan_approvals|UPDATE club.calendar_items|INSERT INTO club.calendar_items/);
  assert.match(sql,/jsonb_build_object\('review_kind','schedule_only'\)/);
  assert.match(sql,/training_delivery_use_independent_sequence/);
  assert.match(sql,/academy_training_delivery_revisions ENABLE ROW LEVEL SECURITY/);
});
test('coach proposal is private until existing Super Admin approval; review queue does not expose other coaches proposals',()=>{
  const submit=sql.slice(sql.indexOf('CREATE FUNCTION club.submit_coach_drill_proposal('),sql.indexOf('END $proposal$;'));
  assert.match(sql,/v_role IS DISTINCT FROM 'coach'/);
  assert.match(sql,/st.is_active AND NOT st.is_archived/);
  assert.match(submit,/'draft',1\) RETURNING/);
  assert.match(submit,/INSERT INTO club.academy_coach_drill_proposals/);
  assert.match(submit,/coach_proposal_not_approved/);
  assert.match(sql,/v_role='coach' AND p.coach_id=v_actor/);
  assert.match(sql,/v_role='super_admin' AND d.status='draft'/);
  assert.match(sql,/club.require_central_academy_drills\(_digest,true\)/);
  assert.match(sql,/academy_coach_drill_proposals ENABLE ROW LEVEL SECURITY/);
});
test('UI and API wire private sequence and new proposal queue separately',()=>{
  const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
  const proposalApi=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyDrillEndpoints.cs');
  assert.match(api,/MapPut\("\/\{planId:guid\}\/delivery"/);
  assert.match(proposalApi,/MapPost\("\/proposals"/);
  assert.match(builder,/schedule_version:plan.version,expected_sequence_version:sequenceVersion/);
  assert.match(builder,/matchingTrainingDeliverySave\(value,plan,sequenceVersion,submitted\)/);
  assert.match(workspace,/deliveryMode=\{!drillReviewMode\}/);
  assert.match(workspace,/typeof plan.delivery_drill_count==='number'/);
  assert.match(library,/canPropose/);
  assert.match(library,/base\+'proposals'/);
});
test('database lifecycle fixtures explicitly cover published autonomy and central review isolation',()=>{
  const delivery=read('../../ops/postgres/academy-training-drill-builder-checks.sql');
  const proposals=read('../../ops/postgres/central-academy-drills-checks.sql');
  for(const marker of ['coach_delivery_invalidated_schedule_approval','stale_delivery_write_accepted','foreign_delivery_group_accepted',
    'delivery_duration_overflow_accepted','revoked_delivery_access_accepted','delivery_leaked_to_preview',
    'published_delivery_change_mutated_schedule','cancelled_delivery_write_accepted'])
    assert.ok(delivery.includes(marker),marker);
  for(const marker of ['coach_proposal_not_private','proposal_owner_isolation_failed','central_review_queue_missing',
    'unreviewed_proposal_visible','coach_proposal_self_approved','proposal_approval_not_propagated','inactive_coach_proposal_accepted'])
    assert.ok(proposals.includes(marker),marker);
});
test('coach autonomy components compile for client and server',()=>{
  for(const [filename,source] of [['AdminTrainingDrillBuilder.svelte',builder],['AdminAcademyTrainingPlans.svelte',workspace],['drills/+page.svelte',library]])
    for(const generate of ['client','server'])compile(source,{filename,generate});
});
