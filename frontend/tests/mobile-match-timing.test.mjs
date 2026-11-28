import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {planGrassrootsRotation,validGroupMatchSettings,validMatchTiming,rotationClock} from '../src/lib/mobile/match-timing.ts';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const ids=n=>Array.from({length:n},(_,i)=>'p'+i);
test('7v7 with nine attending children in 45 minutes gives each 35 minutes and five-minute periods',()=>{
  const plan=planGrassrootsRotation(45,7,ids(9));
  assert.equal(plan.target_seconds,2100);
  assert.equal(plan.periods.length,9);
  assert.equal(plan.min_interval_seconds,300);
  assert.equal(plan.max_interval_seconds,300);
  assert.ok(Object.values(plan.planned_seconds).every(seconds=>seconds===2100));
  assert.deepEqual(plan.periods[0].on_field,ids(7));
  assert.deepEqual(plan.periods[1].enter,['p7','p8']);
  assert.deepEqual(plan.periods[1].leave,['p5','p6']);
});
test('all supported formats keep the field capacity, account for every second and include only attendees',()=>{
  for(const format of [5,7,9,11])for(let n=1;n<=60;n++)for(const duration of [1,37,45,90,180]){
    const present=ids(n),plan=planGrassrootsRotation(duration,format,present);
    const expected=Math.min(n,format);
    assert.equal(plan.short_handed,n<format);
    assert.equal(plan.periods[0].start_seconds,0);
    assert.equal(plan.periods.at(-1).end_seconds,duration*60);
    assert.equal(Object.values(plan.planned_seconds).reduce((a,b)=>a+b,0),expected*duration*60);
    for(const period of plan.periods){
      assert.equal(period.on_field.length,expected);
      assert.equal(new Set(period.on_field).size,expected);
      assert.ok(period.on_field.every(id=>present.includes(id)));
      assert.ok(period.end_seconds>period.start_seconds);
    }
    const times=Object.values(plan.planned_seconds);
    assert.ok(Math.max(...times)-Math.min(...times)<=expected);
    if(n===expected)assert.equal(plan.periods.length,1);
    else assert.ok(plan.max_interval_seconds<=600);
  }
});
test('format and attendance validation reject malformed, cross-owner, duplicate or unselected children',()=>{
  const config={organization_id:'lefteria_fc_academy',host_id:'g',version:1,duration_minutes:45,players_on_field:7};
  assert.equal(validGroupMatchSettings(config,'lefteria_fc_academy','g'),true);
  assert.equal(validGroupMatchSettings(config,'lefteria_fc_academy','other'),false);
  assert.equal(validGroupMatchSettings({...config,players_on_field:6},'lefteria_fc_academy','g'),false);
  const timing={frozen:true,settings_version:1,duration_minutes:45,players_on_field:7,attendance_ids:['p0','p1']};
  assert.equal(validMatchTiming(timing,'lefteria_fc_academy',new Set(ids(3)),false),true);
  assert.equal(validMatchTiming(timing,'lefteria_fc',new Set(ids(3)),false),false);
  assert.equal(validMatchTiming({...timing,attendance_ids:['p0','p0']},'lefteria_fc_academy',new Set(ids(3)),false),false);
  assert.equal(validMatchTiming({...timing,attendance_ids:['unknown']},'lefteria_fc_academy',new Set(ids(3)),false),false);
  assert.equal(validMatchTiming(timing,'lefteria_fc_academy',new Set(ids(3)),true),false);
  for(const args of [[45,6,ids(9)],[0,7,ids(9)],[45,7,[]],[45,7,['a','a']]])assert.throws(()=>planGrassrootsRotation(...args));
  assert.equal(rotationClock(2100),'35:00');
});
test('settings and match attendance are authorized, frozen and not confused with RSVPs',()=>{
  const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128020000_GroupMatchTimingSettings.cs');
  const admin=read('frontend/src/lib/admin/AdminGroupMatchSettings.svelte');
  const workspace=read('frontend/src/lib/mobile/components/MobileMatchWorkspace.svelte');
  const group=read('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
  compile(admin,{filename:'AdminGroupMatchSettings.svelte',generate:'client'});
  compile(workspace,{filename:'MobileMatchWorkspace.svelte',generate:'client'});
  assert.match(sql,/players_on_field IN \(5,7,9,11\)/);
  assert.match(sql,/club\.require_training_drill_group\(v_actor,_host\)/);
  assert.match(sql,/club\.admin_football_allowed\(v_actor,v_role,_owner,v_scope,NULL,true\)/);
  assert.match(sql,/OLD\.timing_frozen/);
  assert.match(sql,/v_day<>\(clock_timestamp\(\) AT TIME ZONE 'Asia\/Nicosia'\)::date/);
  assert.match(sql,/v_plan\.version<>_expected/);
  assert.match(sql,/match_attendance_statistics_invalid/);
  assert.match(group,/<AdminGroupMatchSettings \{organization\} groupId=\{group.id\}/);
  assert.match(workspace,/attendanceDraft=\[\.\.\.\(value.timing.attendance_ids\?\?\[\]\)\]/);
  assert.match(workspace,/planGrassrootsRotation\(duration_minutes,players_on_field,ordered\)/);
  assert.doesNotMatch(admin,/rotation_interval_minutes/);
});
