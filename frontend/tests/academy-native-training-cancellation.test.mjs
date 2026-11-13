import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validTrainingPublicationsPage,validTrainingCancellationResult}
  from '../src/lib/admin/academy-training-plans.ts';

const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const ui=read('../src/lib/admin/AdminAcademyTrainingPlans.svelte');
const api=read('../../backend/src/LefteriaFc.Api/Attendance/AdminAcademyTrainingPlanEndpoints.cs');
const migration=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113130000_AcademyNativeTrainingCancellation.cs');
const lifecycle=read('../../ops/postgres/academy-training-plans-lifecycle-checks.sql');
const grants=read('../../ops/postgres/academy-training-plans-admin-grants.sql');
const calendarReads=read('../../backend/src/LefteriaFc.Infrastructure/ClubCalendar/PostgresMobileCalendarRepository.Reads.cs');
const calendarWrites=read('../../backend/src/LefteriaFc.Infrastructure/ClubCalendar/PostgresMobileCalendarRepository.Writes.cs');
const plan={
  id:'b4228a12-3417-4ef6-917c-cd39de688e63',
  organization_id:'lefteria_fc_academy',group_id:'u12',version:1,
  day:'2026-11-20',title:'Training',status:'draft'};
const reason='Cancelled due to unsafe field';
const base={organization_id:'lefteria_fc_academy',group_id:'u12',
  from:'2026-11-01',to:'2026-11-30',
  publications:[{id:plan.id,event_id:'os-training-'+plan.id,
    published_version:1,is_cancelled:false,cancel_reason:null}]};
test('cancelled and active publication states are strictly validated',()=>{
  assert.equal(validTrainingPublicationsPage(base,'u12',base.from,base.to,[plan]),true);
  const cancelled={...base,publications:[{...base.publications[0],
    is_cancelled:true,cancel_reason:reason}]};
  assert.equal(validTrainingPublicationsPage(cancelled,'u12',base.from,base.to,[plan]),true);
  assert.equal(validTrainingPublicationsPage({...base,
    publications:[{...base.publications[0],is_cancelled:true,cancel_reason:null}]},
    'u12',base.from,base.to,[plan]),false);
  assert.equal(validTrainingPublicationsPage({...base,
    publications:[{...base.publications[0],is_cancelled:false,cancel_reason:reason}]},
    'u12',base.from,base.to,[plan]),false);
  assert.equal(validTrainingPublicationsPage({...base,
    publications:[{...base.publications[0],is_cancelled:true,cancel_reason:'short'}]},
    'u12',base.from,base.to,[plan]),false);
  assert.equal(validTrainingPublicationsPage({...base,
    publications:[{...base.publications[0],event_id:'legacy-training'}]},
    'u12',base.from,base.to,[plan]),false);
  assert.equal(validTrainingPublicationsPage({...base,
    publications:[base.publications[0],base.publications[0]]},
    'u12',base.from,base.to,[plan]),false);
  assert.equal(validTrainingCancellationResult({id:plan.id,
    event_id:'os-training-'+plan.id,organization_id:'lefteria_fc_academy',
    published_version:1,cancelled:true,cancel_reason:reason,
    notifications_sent:false},plan,reason),true);
  assert.equal(validTrainingCancellationResult({id:plan.id,
    event_id:'os-training-'+plan.id,organization_id:'lefteria_fc_academy',
    published_version:1,cancelled:true,cancel_reason:reason,
    notifications_sent:true},plan,reason),false);
});
test('admin confirmation and reason are explicit, with no optimistic cancellation',()=>{
  assert.ok(compile(ui,{filename:'AdminAcademyTrainingPlans.svelte',generate:'server'}).js.code.length>0);
  assert.match(ui,/data-testid="academy-native-cancel-panel"/);
  assert.match(ui,/data-testid="academy-native-cancel-reason"/);
  assert.match(ui,/cancellationConfirmed/);
  assert.match(ui,/validCancellationReason/);
  assert.match(ui,/confirm_cancellation:true/);
  assert.match(ui,/validTrainingCancellationResult\(raw,plan,reason\)/);
  assert.match(ui,/Δεν στάλθηκαν αυτόματες ειδοποιήσεις/);
  assert.match(api,/MapPost\("\/{planId:guid}\/cancel"/);
  assert.match(api,/CancelPublishedTrainingCommand/);
  assert.match(api,/club\.cancel_admin_academy_training_publication/);
});
test('cancel only OS-native canonicals, preserve identity/RSVP history and source',()=>{
  assert.match(migration,/club\.require_academy_training_planner\(_digest,_owner\)/);
  assert.match(migration,/source_collection='os_training'/);
  assert.match(migration,/AND NOT is_archived AND NOT is_cancelled/);
  assert.match(migration,/cancel_reason=_reason/);
  assert.match(migration,/academy_training_publication_cancellations/);
  assert.match(migration,/ENABLE ROW LEVEL SECURITY/);
  assert.match(migration,/notifications_sent',false/);
  assert.match(migration,/CREATE OR REPLACE FUNCTION club\.os_native_training_primary_member/);
  assert.match(migration,/AND NOT c\.is_archived AND c\.member_projection_json IS NOT NULL/);
  assert.match(grants,/cancel_admin_academy_training_publication/);
  assert.match(grants,/academy_training_publication_cancellations'::regclass/);
  assert.match(calendarWrites,/NOT c\.is_archived AND NOT c\.is_cancelled/);
  assert.match(calendarReads,/FROM club\.calendar_items c WHERE NOT c\.is_archived/);
  assert.doesNotMatch(migration,/DELETE FROM club\.calendar_responses|UPDATE club\.calendar_responses|UPDATE migration\.calendar_source_documents/);
});
test('PostgreSQL rollback-only lifecycle covers status, private history and replays',()=>{
  assert.match(lifecycle,/Unconfirmed training cancellation accepted/);
  assert.match(lifecycle,/Cancellation hid event or rewrote historical member RSVP/);
  assert.match(lifecycle,/Authorized manager cancellation history missing/);
  assert.match(lifecycle,/Duplicate native training cancellation was allowed/);
  assert.match(lifecycle,/Revoked manager could cancel/);
  assert.match(lifecycle,/ROLLBACK;/);
});
