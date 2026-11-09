import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109130000_AcademyTrainingAttendance.cs');
const api=read('backend/src/LefteriaFc.Api/Attendance/AdminAcademyAttendanceEndpoints.cs');
const grants=read('ops/postgres/academy-attendance-admin-grants.sql');
const sql=read('ops/postgres/academy-attendance-checks.sql');
const page=read('frontend/src/routes/admin/attendance/+page.svelte');
const catalog=read('frontend/src/lib/admin/module-catalog.ts');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const workflow=read('.github/workflows/rewrite-build.yml');

test('Actual attendance is a new audited overlay, never a source import or RSVP mutation',()=>{
  assert.match(migration,/CREATE TABLE club\.academy_training_attendance \(/);
  assert.match(migration,/CREATE TABLE club\.academy_training_attendance_events \(/);
  assert.match(migration,/REFERENCES club\.calendar_items\(organization_id,kind,id\)/);
  assert.match(migration,/CHECK \(status IN \('present','absent'\)\)/);
  assert.match(migration,/collection='attendance'/);
  assert.match(migration,/r\.collection IN \('availability','event_attendance'\)/);
  assert.match(migration,/coalesce\(m\.status,hist\.status\)/);
  assert.match(migration,/legacy_import/);
  assert.match(migration,/os_manager/);
  assert.match(migration,/INSERT INTO club\.academy_training_attendance_events/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.doesNotMatch(migration,/UPDATE club\.calendar_responses/);
});

test('Only scoped, verified Academy managers can see or mark children',()=>{
  assert.match(migration,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(migration,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(migration,/academy_training_player_eligible\(_owner,_event,_player\)/);
  assert.match(migration,/p\.is_active AND NOT p\.is_archived AND NOT p\.is_transferred/);
  assert.match(migration,/EXISTS\(SELECT 1 FROM club\.calendar_item_guests/);
  assert.match(migration,/club\.player_academy_groups/);
  assert.match(migration,/v_day>\(clock_timestamp\(\) AT TIME ZONE 'Asia\/Nicosia'\)::date/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/organization_id.*Academy/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/attendance:write/);
  assert.match(api,/Count!=1/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.record_admin_academy_training_attendance/);
  assert.match(grants,/attendance_raw_youth_data_exposed/);
  assert.match(grants,/attendance_private_eligibility_helper_exposed/);
  assert.match(sql,/Unverified Academy manager could read youth attendance/);
  assert.match(sql,/Parent RSVP could be recorded as physical presence/);
});

test('Optimistic attendance write and replay do not duplicate ledger events',()=>{
  assert.match(migration,/FOR UPDATE/);
  assert.match(migration,/IF v_existing=_status THEN/);
  assert.match(migration,/IF v_version<>_expected THEN/);
  assert.match(migration,/IF _expected<>0 THEN/);
  assert.match(migration,/IF v_imported=_status THEN/);
  assert.match(migration,/UNIQUE \(organization_id,kind,event_id,player_id,version\)/);
  assert.match(migration,/RETURN jsonb_build_object\('event_id',_event,/);
  assert.match(migration,/cannot_drop|academy_attendance_has_historical_data/);
  assert.doesNotMatch(migration,/INSERT INTO club\.calendar_responses/);
});

test('Admin UI separates RSVP from presence, shows provenance and verifies writes',()=>{
  assert.match(page,/data-testid="admin-academy-attendance"/);
  assert.match(page,/data-testid="attendance-group"/);
  assert.match(page,/data-testid="attendance-present"/);
  assert.match(page,/data-testid="attendance-absent"/);
  assert.match(page,/RSVP:/);
  assert.match(page,/historical_attendance/);
  assert.match(page,/legacy_import/);
  assert.match(page,/canWrite&&register\.writable/);
  assert.match(page,/expected_version:expected/);
  assert.match(page,/validRegister\(payload,event\)/);
  assert.match(page,/data-testid="attendance-denied"/);
  assert.doesNotMatch(page,/@html|innerHTML|localStorage\./);
  assert.match(catalog,/href: '\/admin\/attendance'/);
  assert.match(frame,/href="\/admin\/attendance"/);
  assert.match(program,/app\.MapAdminAcademyAttendance\(\)/);
});

test('CI runs isolated policy probes before the app is considered validated',()=>{
  assert.match(workflow,/academy-attendance-admin-grants\.sql/);
  assert.match(workflow,/academy-attendance-checks\.sql/);
  assert.match(sql,/ROLLBACK;/);
});
