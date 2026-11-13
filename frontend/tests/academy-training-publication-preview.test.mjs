import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validTrainingDistributionPreview}
  from '../src/lib/admin/academy-training-plans.ts';

const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const ui=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
const sql=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113110000_AcademyTrainingPublicationPreview.cs');
const lifecycle=read('../../ops/postgres/academy-training-plans-lifecycle-checks.sql');
const plan={id:'c1e614ef-4fce-4f6e-aedd-4b7ac72b6510',
  organization_id:'lefteria_fc_academy',group_id:'u12',
  day:'2026-11-20',start_time:'17:00',duration_minutes:75,
  title:'Training',location:'Pitch',notes:'Private note',status:'draft',version:2};
const preview={
  id:plan.id,organization_id:plan.organization_id,group_id:plan.group_id,
  day:plan.day,start_time:plan.start_time,duration_minutes:75,title:plan.title,
  location:plan.location,version:2,eligible_player_count:12,
  human_approved:true,published:false
};
test('preview is a strict exact-version projection with no private notes or children',()=>{
  assert.equal(validTrainingDistributionPreview(preview,plan),true);
  assert.equal(validTrainingDistributionPreview({...preview,version:1},plan),false);
  assert.equal(validTrainingDistributionPreview({...preview,group_id:'other'},plan),false);
  assert.equal(validTrainingDistributionPreview({...preview,published:true},plan),false);
  assert.equal(validTrainingDistributionPreview({...preview,human_approved:false},plan),false);
  assert.equal(validTrainingDistributionPreview({...preview,eligible_player_count:-1},plan),false);
  assert.equal(validTrainingDistributionPreview({...preview,notes:'secret'},plan),false);
  assert.equal(validTrainingDistributionPreview({...preview,players:['child']},plan),false);
});
test('manager UI requires approval and a scoped read-only preview before explicit publication',()=>{
  assert.ok(compile(ui,{filename:'AdminAcademyTrainingPlans.svelte',generate:'server'}).js.code.length>0);
  assert.match(ui,/approvedIds\.includes\(plan\.id\)/);
  assert.match(ui,/publication-preview\?/);
  assert.match(ui,/data-testid="training-distribution-preview"/);
  assert.match(ui,/validTrainingDistributionPreview\(raw,plan\)/);
  assert.match(ui,/Δεν δημοσιεύτηκε ακόμη/);
  assert.match(ui,/!approvedIds.includes\(plan.id\)/);
  assert.match(ui,/publishConfirmed/);
  assert.match(ui,/confirm_publication:true/);
});
test('backend reauthorizes current manager, exact approval and active roster only',()=>{
  assert.match(sql,/club\.require_academy_training_planner\(_digest,_owner\)/);
  assert.match(sql,/AND a\.plan_version=v_plan\.version/);
  assert.match(sql,/v_plan\.version<>_expected/);
  assert.match(sql,/p\.is_active/);
  assert.match(sql,/NOT p\.is_archived AND NOT p\.is_transferred/);
  assert.match(sql,/p\.primary_academy_group_id=v_plan\.group_id/);
  assert.match(sql,/eligible_player_count/);
  assert.match(sql,/published',false/);
  assert.doesNotMatch(sql,/jsonb_build_object\([^]*'notes'|SELECT p\.id INTO/);
  assert.match(api,/MapGet\("\/{planId:guid}\/publication-preview"/);
  assert.match(lifecycle,/Human-approved distribution preview exposed private youth data/);
  assert.match(lifecycle,/Edited plan still previewable using stale approval/);
  assert.match(lifecycle,/Revoked manager could preview private training recipients/);
});
