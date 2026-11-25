import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL(p,import.meta.url),'utf8');
const sql=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125093000_AcademyTrainingExactTerritory.cs');
const prior=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125091000_AcademyTrainingDrillBuilder.cs');
const lifecycle=read('../../ops/postgres/academy-training-plans-lifecycle-checks.sql');
const fn=name=>{
  const start=sql.indexOf('CREATE OR REPLACE FUNCTION club.'+name+'(');
  assert.ok(start>=0,name);
  const next=sql.indexOf('CREATE OR REPLACE FUNCTION club.',start+1);
  return sql.slice(start,next===-1?undefined:next);
};
test('all group-addressed native plan routes require exact live territory before querying or writing plans',()=>{
  for(const name of ['read_admin_academy_training_plans','write_admin_academy_training_plan',
    'read_admin_academy_training_plan_approvals','read_admin_academy_training_publications']){
    const body=fn(name);
    assert.match(body,/club.require_academy_training_planner\(_digest,_owner\)/);
    assert.match(body,/PERFORM club.require_training_drill_group\(v_actor,_group\)/);
    const gate=body.indexOf('PERFORM club.require_training_drill_group');
    const rows=body.search(/FROM club.academy_training_plan_drafts|INSERT INTO club.academy_training_plan_drafts|FROM club.academy_training_plan_publications/);
    assert.ok(rows>gate,name+' checks group before accessing private records');
  }
  assert.match(prior,/JOIN club.football_cities c ON c.id=a.city_id AND c.is_active/);
  assert.match(prior,/JOIN club.football_locations l ON l.id=a.location_id AND l.city_id=a.city_id AND l.is_active/);
  assert.match(prior,/club.admin_football_allowed\(_actor,v_role,'lefteria_fc_academy',v_scope,v_location,true\)/);
});
test('ID-addressed lifecycle resolves exact group and checks it before locking the plan',()=>{
  for(const name of ['approve_admin_academy_training_plan','preview_admin_academy_training_distribution',
    'publish_admin_academy_training_plan','cancel_admin_academy_training_publication']){
    const body=fn(name);
    assert.match(body,/SELECT group_id INTO v_group FROM club.academy_training_plan_drafts/);
    assert.match(body,/PERFORM club.require_training_drill_group\(v_actor,v_group\)/);
    assert.match(body,/WHERE id=_plan AND organization_id=_owner AND group_id=v_group FOR (UPDATE|SHARE)/);
    assert.ok(body.indexOf('PERFORM club.require_training_drill_group')<body.indexOf('SELECT * INTO v_plan'));
  }
  assert.match(sql,/throw new System.NotSupportedException/);
  assert.match(fn('approve_admin_training_drill_plan'),/btrim\(_member_summary,' '\|\|chr\(10\)\)/);
});
test('rollback-only lifecycle covers sibling location, foreign city, active grants and all operations',()=>{
  for(const marker of ['same_city_foreign_site_list_accepted','foreign_city_plan_create_accepted',
    'same_city_foreign_site_edit_accepted','same_city_foreign_site_approval_accepted','foreign_city_approval_list_accepted',
    'same_city_foreign_site_publication_list_accepted','same_city_foreign_site_preview_accepted','foreign_city_publish_accepted',
    'same_city_foreign_site_cancel_accepted','unmapped_group_list_accepted','inactive_location_list_accepted',
    'revoked_territory_list_accepted','city_manager_valid_site_denied']){
    assert.ok(lifecycle.includes(marker),marker);
  }
  assert.match(lifecycle,/ROLLBACK;/);
});

test('attendance register and writes check every explicit host group, not just Academy membership',()=>{
  const attendance=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261125094000_AcademyTrainingAttendanceExactTerritory.cs');
  const grants=read('../../ops/postgres/academy-attendance-admin-grants.sql');
  const checks=read('../../ops/postgres/academy-attendance-lifecycle-checks.sql');
  assert.match(attendance,/AND s.scope_kind<>'group'/);
  assert.match(attendance,/ORDER BY s.academy_group_id FOR SHARE/);
  assert.match(attendance,/PERFORM club.require_training_drill_group\(_actor,v_group\)/);
  assert.match(attendance,/RETURN v_groups>0/);
  assert.match(attendance,/PERFORM club.require_training_drill_group\(v_actor,_group\)/);
  assert.ok((attendance.match(/AND club.admin_training_event_scope_allowed\(v_actor,_owner,c.id\)/g)||[]).length===2);
  assert.ok((attendance.match(/IF NOT club.admin_training_event_scope_allowed\(v_actor,_owner,_event\)/g)||[]).length===2);
  assert.match(grants,/REVOKE ALL ON FUNCTION club.admin_training_event_scope_allowed\(uuid,text,text\) FROM PUBLIC/);
  for(const marker of ['foreign_attendance_list_accepted','foreign_attendance_roster_accepted','foreign_attendance_write_accepted',
    'multi_site_event_list_exposed','multi_site_event_roster_exposed','inactive_attendance_site_accepted'])
    assert.ok(checks.includes(marker),marker);
});
