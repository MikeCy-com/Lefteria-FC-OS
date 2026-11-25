import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {academyCoachWeek} from '../src/lib/admin/academy-schedule.ts';
import {completeTrainingSession,gameMinutes,drillRemainingSeconds,formatDrillTimer} from '../src/lib/admin/academy-training-drills.ts';
import {validDrill} from '../src/lib/admin/central-academy-drills.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const weekly=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125120000_AcademyWeeklyCompleteSession.cs');
const variants=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125121000_AcademyDrillVariantsAndRevisions.cs');
const builder=read('../src/lib/admin/AdminTrainingDrillBuilder.svelte');
const library=read('../src/routes/admin/drills/+page.svelte');
const schedule=read('../src/routes/admin/schedule/+page.svelte');
const planUI=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const base={id:'51ee2557-2727-44f6-a73a-2600a0767a22',organization_id:'lefteria_fc_academy',
  title:'Passing',category:'technical',age_band:'all',duration_minutes:20,objective:'Accuracy',instructions:'Pass in pairs',status:'approved',version:2};
const id='19ee2557-2727-44f6-a73e-2600a0767a22';
const snapshot=(drill_id,category,duration_minutes)=>({drill_id,drill_version:2,category,duration_minutes,
  title:'Training',age_band:'all',objective:'Practice',instructions:'Follow instructions'});
test('rolling seven days follow Cyprus local date even across month/year boundaries',()=>{
  assert.deepEqual(academyCoachWeek(new Date('2026-11-30T22:30:00Z')),{start:'2026-12-01',end:'2026-12-07'});
  assert.deepEqual(academyCoachWeek(new Date('2026-12-29T12:00:00Z')),{start:'2026-12-29',end:'2027-01-04'});
});
test('complete training allocates exact duration and at least 20 game minutes across multiple drills',()=>{
  const a=snapshot(base.id,'technical',40),b=snapshot(id,'game',20);
  assert.equal(gameMinutes([a,b]),20);
  assert.equal(completeTrainingSession([a,b],60),true);
  assert.equal(completeTrainingSession([{...a,duration_minutes:41},{...b,duration_minutes:19}],60),false);
  assert.equal(completeTrainingSession([b],60),false);
  assert.equal(completeTrainingSession([{...a,duration_minutes:60}],60),false);
  assert.equal(completeTrainingSession([a,{...b,duration_minutes:21}],60),false);
  assert.equal(completeTrainingSession([a,b,b],80),false);
  const c=snapshot('d51f4fef-28ea-42b3-b12e-cd05f1d42eb9','game',10);
  assert.equal(completeTrainingSession([a,{...b,duration_minutes:10},c],60),true);
});
test('countdown follows absolute deadline, clamps elapsed time and formats minutes',()=>{
  assert.equal(drillRemainingSeconds(1200000,0),1200);
  assert.equal(drillRemainingSeconds(1200000,1199001),1);
  assert.equal(drillRemainingSeconds(1200000,1200001),0);
  assert.equal(formatDrillTimer(1200),'20:00');
  assert.equal(formatDrillTimer(65),'01:05');
  assert.equal(formatDrillTimer(0),'00:00');
});
test('difficulty lineage accepted centrally, review metadata allowed only in proposals',()=>{
  const variant={...base,id,variant_of:base.id,variant_base_version:2,difficulty:'advanced'};
  assert.equal(validDrill(base),true);
  assert.equal(validDrill(variant),true);
  assert.equal(validDrill({...variant,difficulty:'expert'}),false);
  assert.equal(validDrill({...variant,variant_of:id}),false);
  const proposal={...variant,status:'draft',version:1,proposal_kind:'variant',base_drill_id:base.id,base_version:2,applied_version:null};
  assert.equal(validDrill(proposal),false);
  assert.equal(validDrill(proposal,true),true);
  const revision={...base,id,status:'archived',difficulty:'intermediate',proposal_kind:'revision',base_drill_id:base.id,base_version:2,applied_version:3};
  assert.equal(validDrill(revision,true),true);
  assert.equal(validDrill({...revision,applied_version:4},true),false);
  assert.equal(validDrill({...revision,status:'approved'},true),false);
  assert.equal(validDrill({...revision,proposal_kind:['revision']},true),false);
});
test('database enforces coach horizon, complete delivery, and exact approved base review',()=>{
  assert.match(weekly,/today\+6/);
  assert.match(weekly,/v_role='coach' AND \(_archive OR NOT club.coach_training_week_allows\(_day\)\)/);
  assert.match(weekly,/club.require_complete_training_delivery\(v_result,v_plan.duration_minutes\)/);
  assert.match(weekly,/club.training_delivery_game_minutes\(_items\)>=20/);
  assert.match(weekly,/club.require_complete_training_delivery\(club.training_delivery_view\(v_plan\)->'drills',v_plan.duration_minutes\)/);
  assert.match(weekly,/coach_use_weekly_delivery/);
  assert.match(variants,/id=_base AND status='approved' AND version=_base_version FOR SHARE/);
  assert.match(variants,/club.require_central_academy_drills\(_digest,true\)/);
  assert.match(variants,/id=v_proposal.base_drill_id AND status='approved' AND version=_base_version FOR UPDATE/);
  assert.match(variants,/drill_revision_base_changed/);
  assert.match(variants,/use_exact_revision_review/);
  assert.doesNotMatch(variants,/UPDATE club.academy_coaching_drill_versions SET/);
});
test('UI wiring and rollback-only scenarios cover actual requested workflow',()=>{
  const sessions=read('../../ops/postgres/academy-training-drill-builder-checks.sql');
  const drills=read('../../ops/postgres/central-academy-drills-checks.sql');
  assert.match(schedule,/academyCoachWeek\(\)/);
  assert.match(builder,/completeTrainingSession\(sequence,plan.duration_minutes\)/);
  assert.match(builder,/data-testid="training-execution-timer"/);
  assert.match(builder,/deadline=Date.now\(\)\+remainingMs/);
  assert.match(builder,/clearInterval\(ticker\)/);
  assert.match(planUI,/plan.delivery_complete!==true/);
  assert.match(library,/proposeFrom\(row,'revision'\)/);
  assert.match(library,/proposeFrom\(row,'variant'\)/);
  assert.match(library,/apply-revision/);
  for(const marker of ['coach_month_ahead_accepted','incomplete_session_submitted','nineteen_game_minutes_accepted','underallocated_session_accepted'])
    assert.ok(sessions.includes(marker),marker);
  for(const marker of ['pending_revision_mutated_approved_base','unreviewed_variant_visible','coach_applied_own_revision','stale_base_revision_applied','approved_variant_lineage_missing'])
    assert.ok(drills.includes(marker),marker);
});
test('weekly coach session and variant components compile',()=>{
  for(const [filename,source] of [['AdminTrainingDrillBuilder.svelte',builder],['drills/+page.svelte',library],['schedule/+page.svelte',schedule],['AdminAcademyTrainingPlans.svelte',planUI]])
    for(const generate of ['client','server'])compile(source,{filename,generate});
});
