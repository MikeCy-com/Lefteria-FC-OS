import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validTrainingDistributionPreview} from '../src/lib/admin/academy-training-plans.ts';

const ui=readFileSync(new URL('../src/lib/admin/AdminAcademyTrainingPlans.svelte',import.meta.url),'utf8');
const plan={id:'c1e614ef-4fce-4f6e-aedd-4b7ac72b6510',organization_id:'lefteria_fc_academy',
  group_id:'u12',day:'2026-12-01',start_time:'17:00',duration_minutes:75,
  title:'Training',location:'Pitch',notes:'Private',status:'draft',version:2};
const preview={id:plan.id,organization_id:plan.organization_id,group_id:plan.group_id,
  day:plan.day,start_time:plan.start_time,duration_minutes:plan.duration_minutes,
  title:plan.title,location:plan.location,version:plan.version,eligible_player_count:12,
  human_approved:true,published:false};
test('recipient preview rejects all unknown keys, not only known sensitive fields',()=>{
  assert.equal(validTrainingDistributionPreview(preview,plan),true);
  for(const key of ['children','guardian_names','contact_details','debug','private_notes','recipient_ids']){
    assert.equal(validTrainingDistributionPreview({...preview,[key]:[]},plan),false);
  }
  const {location,...missing}=preview;
  assert.equal(validTrainingDistributionPreview(missing,plan),false);
});
test('human review is bound to draft id and version, and consumed before the request',()=>{
  compile(ui,{filename:'AdminAcademyTrainingPlans.svelte',generate:'server'});
  compile(ui,{filename:'AdminAcademyTrainingPlans.svelte',generate:'client'});
  assert.match(ui,/reviewKey=\(plan:TrainingPlanDraft\)=>\`\$\{plan.id\}:\$\{plan.version\}\`/);
  assert.match(ui,/checked=\{reviewConfirmed===reviewKey\(plan\)\}/);
  assert.match(ui,/reviewConfirmed!==reviewKey\(plan\)/);
  assert.match(ui,/rows.some\(row=>row.id===plan.id&&row.version===plan.version&&row.status==='draft'\)/);
  const approval=ui.slice(ui.indexOf('  async function approve('),ui.indexOf('  async function save('));
  assert.ok(approval.indexOf('reviewConfirmed=null;')>=0);
  assert.ok(approval.indexOf('reviewConfirmed=null;')<approval.indexOf('await fetch'));
  assert.doesNotMatch(ui,/bind:checked=\{reviewConfirmed\}/);
  assert.match(ui,/\{#if !rows.length&&!error\}/);
});
