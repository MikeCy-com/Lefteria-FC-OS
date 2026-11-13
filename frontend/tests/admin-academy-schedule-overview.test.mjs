import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {academyMonthWindow,isAcademyScheduleGroupList,isAcademyTrainingList,upcomingAcademySessions}
  from '../src/lib/admin/academy-schedule.ts';

const component=readFileSync(new URL('../src/routes/admin/schedule/+page.svelte',import.meta.url),'utf8');
const catalog=readFileSync(new URL('../src/lib/admin/module-catalog.ts',import.meta.url),'utf8');
const scope='lefteria_fc_academy';
const good={organization_id:scope,group_id:'u12',from:'2026-11-01',to:'2026-11-30',total:3,
  sessions:[{id:'t1',day:'2026-11-19',title:'Training 1',is_cancelled:false,is_archived:false},
    {id:'t2',day:'2026-11-20',title:'Training 2',is_cancelled:true,is_archived:false},
    {id:'t3',day:'2026-11-03',title:'Training 3',is_cancelled:false,is_archived:false}]};
test('Academy schedule validates organization, group, date and session identifiers',()=>{
  assert.deepEqual(academyMonthWindow('2026-02'),{start:'2026-02-01',end:'2026-02-28'});
  assert.deepEqual(academyMonthWindow('2028-02'),{start:'2028-02-01',end:'2028-02-29'});
  assert.equal(academyMonthWindow('2026-13'),null);
  assert.equal(isAcademyTrainingList(good,'u12','2026-11-01','2026-11-30'),true);
  assert.equal(isAcademyTrainingList({...good,organization_id:'lefteria_fc'},'u12',good.from,good.to),false);
  assert.equal(isAcademyTrainingList({...good,group_id:'u10'},'u12',good.from,good.to),false);
  assert.equal(isAcademyTrainingList({...good,sessions:[{...good.sessions[0],day:'2026-12-20'}]},'u12',good.from,good.to),false);
  assert.equal(isAcademyTrainingList({...good,sessions:[good.sessions[0],good.sessions[0]]},'u12',good.from,good.to),false);
  assert.equal(isAcademyTrainingList({...good,total:0},'u12',good.from,good.to),false);
  assert.deepEqual(upcomingAcademySessions(good,'2026-11-13').map(x=>x.id),['t1']);
});
test('Academy schedule rejects foreign, duplicate or invalid groups',()=>{
  const dir={organization_id:scope,ready:true,groups:[{id:'u12',name:'U12',kind:'academy_group',active_players:11}]};
  assert.equal(isAcademyScheduleGroupList(dir),true);
  assert.equal(isAcademyScheduleGroupList({...dir,organization_id:'lefteria_fc'}),false);
  assert.equal(isAcademyScheduleGroupList({...dir,groups:[dir.groups[0],dir.groups[0]]}),false);
});
test('admin schedule uses real cookie-scoped existing backend and only manager grant',()=>{
  assert.match(component,/\['manager','super_admin'\]/);
  assert.match(component,/hasAdminPermission\(\$auth\.session\.user\.permissions,'attendance:read'\)/);
  assert.match(component,/\/api\/admin\/football\/audiences\?organization_id=lefteria_fc_academy/);
  assert.match(component,/\/api\/admin\/academy\/training\/\?/);
  assert.match(component,/credentials:'same-origin',cache:'no-store'/);
  assert.match(component,/onExpired|auth\.bootstrap\(\)/);
  assert.match(catalog,/id: 'academy_schedule'/);
  assert.match(catalog,/href: '\/admin\/schedule'/);
  assert.match(catalog,/item\.id === 'academy_schedule' \|\| item\.id === 'attendance'/);
  assert.doesNotMatch(component,/method:'POST'|method:'DELETE'|sampleTraining|demoTraining|localStorage|playerPhone/);
  assert.ok(compile(component,{filename:'AdminAcademySchedule.svelte',generate:'server'}).js.code.length>0);
});
