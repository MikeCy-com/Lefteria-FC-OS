import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validTrainingPublicationsPage,validPublishedTrainingResult}
  from '../src/lib/admin/academy-training-plans.ts';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const page=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const migration=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113120000_AcademyTrainingCanonicalPublication.cs');
const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
const lifecycle=read('../../ops/postgres/academy-training-plans-lifecycle-checks.sql');
const access=read('../../backend/src/LefteriaFc.Infrastructure/ClubCalendar/CalendarAccessSql.cs');
const grants=read('../../ops/postgres/academy-training-plans-admin-grants.sql');
const mobileGrants=read('../../ops/postgres/identity-app-grants.sql');
const plan={id:'51882d43-a9c9-4edf-92fa-928b47b67af0',
  organization_id:'lefteria_fc_academy',group_id:'u12',
  day:'2026-11-20',start_time:'17:00',duration_minutes:75,
  title:'Training',location:'Pitch',notes:'Do not publish notes',
  status:'draft',version:2};
const canonical='os-training-'+plan.id;
test('canonical publication status is scoped to exact current draft',()=>{
  const actual={organization_id:'lefteria_fc_academy',group_id:'u12',
    from:'2026-11-01',to:'2026-11-30',
    publications:[{id:plan.id,event_id:canonical,published_version:2,
      is_cancelled:false,cancel_reason:null}]};
  assert.equal(validTrainingPublicationsPage(actual,'u12',actual.from,actual.to,[plan]),true);
  assert.equal(validTrainingPublicationsPage({...actual,group_id:'other'},'u12',actual.from,actual.to,[plan]),false);
  assert.equal(validTrainingPublicationsPage({...actual,publications:[{...actual.publications[0],published_version:1}]},'u12',actual.from,actual.to,[plan]),false);
  assert.equal(validTrainingPublicationsPage({...actual,publications:[...actual.publications,...actual.publications]},'u12',actual.from,actual.to,[plan]),false);
  assert.equal(validTrainingPublicationsPage({...actual,publications:[{...actual.publications[0],event_id:'legacy'}]},'u12',actual.from,actual.to,[plan]),false);
  assert.equal(validPublishedTrainingResult({id:plan.id,
    organization_id:'lefteria_fc_academy',event_id:canonical,
    event_type:'training',published_version:2,published:true,
    notifications_sent:false},plan),true);
  assert.equal(validPublishedTrainingResult({id:plan.id,
    organization_id:'lefteria_fc_academy',event_id:canonical,
    event_type:'training',published_version:2,published:true,
    notifications_sent:true},plan),false);
});
test('explicitly confirmed publication binds existing human-approved version',()=>{
  assert.match(api,/MapPost\("\/{planId:guid}\/publish"/);
  assert.match(api,/ConfirmPublication/);
  assert.match(api,/club\.publish_admin_academy_training_plan/);
  assert.match(page,/publishConfirmed/);
  assert.match(page,/preview\.eligible_player_count<1/);
  assert.match(page,/confirm_publication:true/);
  assert.match(page,/validPublishedTrainingResult\(raw,plan\)/);
  assert.match(page,/validTrainingPublicationsPage\(published,groupId,from,to,raw\.plans\)/);
  assert.match(page,/Δεν στάλθηκαν ειδοποιήσεις/);
  assert.ok(compile(page,{filename:'AdminAcademyTrainingPlans.svelte',generate:'server'}).js.code.length>0);
});
test('OS-native calendar source has independent provenance, group scope and no private notes',()=>{
  assert.match(migration,/source_collection IN\s*\('training_sessions','os_training'\)/);
  assert.match(migration,/collection IN \('fixtures','events','training_sessions'/);
  assert.match(migration,/NOT EXISTS\(SELECT 1 FROM club\.academy_training_plan_approvals/);
  assert.match(migration,/p\.primary_academy_group_id=v_plan\.group_id/);
  assert.match(migration,/v_active=0/);
  assert.match(migration,/migration\.calendar_source_documents/);
  assert.match(migration,/VALUES\('os_training',v_source/);
  assert.match(migration,/INSERT INTO club\.calendar_items/);
  assert.match(migration,/INSERT INTO club\.calendar_item_scopes/);
  assert.match(migration,/scope_kind/);
  assert.match(migration,/club\.academy_training_plan_publications/);
  assert.match(migration,/prevent_published_academy_plan_edits/);
  assert.match(migration,/notifications_sent',false/);
  assert.match(access,/c\.source_collection='os_training'/);
  assert.match(access,/club\.os_native_training_primary_member\(c\.organization_id,c\.id,p\.id\)/);
  assert.match(migration,/CREATE FUNCTION club\.os_native_training_primary_member/);
  assert.match(migration,/c\.source_collection='os_training'/);
  assert.match(migration,/player\.primary_academy_group_id=sc\.record_id/);
  assert.match(mobileGrants,/GRANT EXECUTE ON FUNCTION club\.os_native_training_primary_member/);
  assert.doesNotMatch(mobileGrants,/primary_academy_group_id, is_active/);
  assert.doesNotMatch(mobileGrants,/GRANT SELECT ON club\.players TO/);
  assert.doesNotMatch(migration,/v_plan\.notes[,)]|notes',v_plan\.notes/);
  assert.doesNotMatch(migration,/INSERT INTO club\.calendar_responses/);
  assert.match(grants,/club\.publish_admin_academy_training_plan/);
  assert.match(grants,/club\.academy_training_plan_publications'::regclass/);
});
test('rollback-only database lifecycle proves registered events, eligibility and RSVP',()=>{
  assert.match(lifecycle,/Unapproved native training was published/);
  assert.match(lifecycle,/Duplicate canonical event publication accepted/);
  assert.match(lifecycle,/Published plan could still be edited/);
  assert.match(lifecycle,/Canonical published training missing from Academy register/);
  assert.match(lifecycle,/Published group athlete missing from authorized attendance/);
  assert.match(lifecycle,/Canonical published training does not accept RSVP reference/);
  assert.match(lifecycle,/Revoked manager could publish/);
  assert.match(lifecycle,/ROLLBACK;/);
});
