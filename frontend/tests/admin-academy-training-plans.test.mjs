import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {
  validTrainingPlanDraft,validTrainingPlanPage,validTrainingPlanForm,
  matchingTrainingPlanWrite
} from '../src/lib/admin/academy-training-plans.ts';

const read=relative=>readFileSync(new URL(relative,import.meta.url),'utf8');
const panel=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const page=read('../src/routes/admin/schedule/+page.svelte');
const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
const migration=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113080000_AcademyTrainingPrivatePlans.cs');
const grants=read('../../ops/postgres/academy-training-plans-admin-grants.sql');
const sample={
  id:'a84ebec2-7e8e-4636-8551-a1c6575cb8a8',
  organization_id:'lefteria_fc_academy',group_id:'u12',day:'2026-11-20',
  start_time:'17:00',duration_minutes:75,title:'Τεχνική προπόνηση',
  location:'Field 1',notes:'Private manager note',
  status:'draft',version:1
};
test('training drafts validate exact owner, group, schedule, provenance and version',()=>{
  assert.equal(validTrainingPlanDraft(sample,'u12'),true);
  assert.equal(validTrainingPlanDraft({...sample,organization_id:'lefteria_fc'},'u12'),false);
  assert.equal(validTrainingPlanDraft(sample,'u13'),false);
  assert.equal(validTrainingPlanDraft({...sample,status:'published'},'u12'),false);
  assert.equal(validTrainingPlanDraft({...sample,start_time:'27:30'},'u12'),false);
  assert.equal(validTrainingPlanDraft({...sample,version:0},'u12'),false);
  assert.equal(validTrainingPlanDraft({...sample,notes:'secret\n'},'u12'),false);
  assert.equal(matchingTrainingPlanWrite(sample,'u12',sample.id,1,'draft'),true);
  assert.equal(matchingTrainingPlanWrite(sample,'u12',sample.id,2,'draft'),false);
  assert.equal(matchingTrainingPlanWrite(sample,'u12',undefined,1,'archived'),false);
});
test('private plans list rejects duplicate, foreign, out-of-range or missing records',()=>{
  const page={organization_id:'lefteria_fc_academy',
    group_id:'u12',from:'2026-11-01',to:'2026-11-30',total:1,plans:[sample]};
  assert.equal(validTrainingPlanPage(page,'u12',page.from,page.to),true);
  assert.equal(validTrainingPlanPage({...page,total:2},'u12',page.from,page.to),false);
  assert.equal(validTrainingPlanPage({...page,plans:[sample,sample],total:2},'u12',page.from,page.to),false);
  assert.equal(validTrainingPlanPage({...page,plans:[{...sample,day:'2026-12-01'}]},'u12',page.from,page.to),false);
  assert.equal(validTrainingPlanPage({...page,organization_id:'lefteria_fc'},'u12',page.from,page.to),false);
  assert.equal(validTrainingPlanForm({
    day:sample.day,start_time:sample.start_time,duration_minutes:75,
    title:sample.title,location:'',notes:''
  }),true);
  assert.equal(validTrainingPlanForm({
    day:sample.day,start_time:sample.start_time,duration_minutes:0,
    title:sample.title,location:'',notes:''
  }),false);
});
test('manager-only editor compiles and writes via existing authenticated admin boundary',()=>{
  assert.ok(compile(panel,{filename:'AdminAcademyTrainingPlans.svelte',generate:'server'}).js.code.length>0);
  assert.match(page,/AdminAcademyTrainingPlans/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions,'training:write'\)/);
  assert.match(panel,/X-Lefteria-Admin/);
  assert.match(panel,/credentials:'same-origin',cache:'no-store'/);
  assert.match(panel,/matchingTrainingPlanWrite/);
  assert.match(panel,/archiveConfirmed/);
  assert.match(panel,/Δεν δημοσιεύτηκε|ΔΕΝ δημοσιεύονται/);
  assert.doesNotMatch(panel,/localStorage|mockTraining|fetch\('https:\/\//);
});
test('backend protects imported calendar and locks current manager/grant in SQL',()=>{
  assert.match(api,/training:write/);
  assert.match(api,/training:read/);
  assert.match(api,/manager" or "super_admin"/);
  assert.match(api,/MapPut\("\/{planId:guid}"/);
  assert.match(migration,/identity\.lock_admin_club_authority/);
  assert.match(migration,/ENABLE ROW LEVEL SECURITY/);
  assert.match(migration,/academy_training_plan_events/);
  assert.match(migration,/training_plan_version_conflict/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.write_admin_academy_training_plan/);
  assert.match(grants,/training_plans_table_exposure/);
  assert.doesNotMatch(migration,/UPDATE club\.calendar_items|DELETE FROM club\.calendar_items|INSERT INTO club\.calendar_items|UPDATE club\.calendar_responses/);
});
