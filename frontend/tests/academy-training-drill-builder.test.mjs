import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validTrainingDrillSnapshot,validTrainingDrillSequence,validTrainingDrillPlan,
  plannedMinutes,snapshotDrill,drillSelections,matchingTrainingDrillSave} from '../src/lib/admin/academy-training-drills.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const plan={id:'e76aa6ed-96c5-4a7c-8df4-d44ef2809811',organization_id:'lefteria_fc_academy',
  group_id:'ci-group',version:2,status:'draft',duration_minutes:60,day:'2026-12-01',start_time:'17:00',
  title:'Training',location:'Pitch',notes:'Private',drill_count:2};
const drill={id:'51ee2557-2727-44f6-a73a-2600a0767a22',organization_id:plan.organization_id,
  title:'Passing',category:'technical',age_band:'u9-u11',duration_minutes:20,objective:'Accuracy',
  instructions:'Pass in pairs',status:'approved',version:3};
const first=snapshotDrill(drill),second={...first,drill_id:'19ee2557-2727-44f6-a73a-2600a0767a22',duration_minutes:10};
const sequence=[first,second];
const response={id:plan.id,organization_id:plan.organization_id,group_id:plan.group_id,
  version:plan.version,status:plan.status,duration_minutes:plan.duration_minutes,drills:sequence};
test('exercise selections retain exact version and order without sending coaching text',()=>{
  assert.equal(validTrainingDrillSnapshot(first),true);
  assert.equal(plannedMinutes(sequence),30);
  assert.deepEqual(drillSelections(sequence),[{drill_id:first.drill_id,drill_version:3,duration_minutes:20},
    {drill_id:second.drill_id,drill_version:3,duration_minutes:10}]);
  assert.equal(validTrainingDrillSequence(sequence,60),true);
  assert.equal(validTrainingDrillSequence([first,first],60),false);
  assert.equal(validTrainingDrillSequence([{...first,duration_minutes:61}],60),false);
  assert.equal(validTrainingDrillSequence([{...first,duration_minutes:NaN}],60),false);
  assert.equal(validTrainingDrillSequence([{...first,player_id:'private'}],60),false);
});
test('plan read and write must match scope, exact version, full frozen content and order',()=>{
  assert.equal(validTrainingDrillPlan(response,plan,2),true);
  for(const bad of [{...response,group_id:'other'},{...response,version:3},{...response,private_notes:'secret'},
    {...response,drills:[{...first,coach_notes:'secret'}]},{...response,drills:[first,first]}])
    assert.equal(validTrainingDrillPlan(bad,plan,2),false);
  assert.equal(matchingTrainingDrillSave({...response,version:3},plan,sequence),true);
  assert.equal(matchingTrainingDrillSave({...response,version:3,drills:[second,first]},plan,sequence),false);
  assert.equal(matchingTrainingDrillSave({...response,version:3,drills:[{...first,title:'changed'},second]},plan,sequence),false);
  assert.equal(matchingTrainingDrillSave({...response,version:2},plan,sequence),false);
});
test('private exercise builder has exact territory and publication gates at database boundary',()=>{
  const sql=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125091000_AcademyTrainingDrillBuilder.cs');
  const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
  const lifecycle=read('../../ops/postgres/academy-training-drill-builder-checks.sql');
  assert.match(sql,/admin_football_allowed\(_actor,v_role,'lefteria_fc_academy',v_scope,v_location,true\)/);
  assert.match(sql,/FOR SHARE OF a,s,c,l/);
  assert.match(sql,/status='approved' AND version=v_version FOR SHARE/);
  assert.match(sql,/drill_snapshots=v_result,version=version\+1/);
  assert.match(sql,/training_drill_review_not_enabled/);
  assert.match(sql,/training_drills_exceed_duration/);
  assert.match(sql,/academy_training_drill_event_snapshot BEFORE INSERT/);
  assert.match(sql,/CASE WHEN v_drill_scope THEN jsonb_array_length\(p.drill_snapshots\) ELSE NULL END/);
  assert.match(api,/body.Drills.Length>24/);
  assert.match(api,/JsonUnmappedMemberHandling.Disallow/);
  for(const marker of ['foreign_site_write_accepted','foreign_site_read_accepted','exercise_edit_preserved_stale_approval','frozen_snapshot_lost','revoked_access_accepted','schedule_only_approval_missing'])
    assert.ok(lifecycle.includes(marker));
});
test('editor uses approved central catalogue and exact server response, no local persistence',()=>{
  const ui=read('../src/lib/admin/AdminTrainingDrillBuilder.svelte');
  const workspace=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
  for(const [filename,source] of [['AdminTrainingDrillBuilder.svelte',ui],['AdminAcademyTrainingPlans.svelte',workspace]])
    for(const generate of ['client','server'])compile(source,{filename,generate});
  assert.match(ui,/status:'approved'/);
  assert.match(ui,/matchingTrainingDrillSave\(value,plan,submitted\)/);
  assert.match(ui,/drills:drillSelections\(submitted\)/);
  assert.match(ui,/sequence=items/);
  assert.match(workspace,/deliveryMode=\{!drillReviewMode\}/);
  assert.match(workspace,/\{#key actor\+':'\+groupId\+':'\+drillTarget.id\+':'\+drillTarget.version\}/);
  assert.doesNotMatch(ui,/localStorage|sessionStorage/);
});
