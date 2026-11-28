import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {calendarTraining,calendarFixtures,calendarMatchDrafts,sortCalendar} from '../src/lib/admin/group-calendar.ts';
import {groupWorkspaceAccess} from '../src/lib/admin/workspace-access.ts';
const owner='lefteria_fc_academy',group='group-a',from='2026-11-01',to='2026-11-30';
test('calendar preserves source identity, omits archived sessions and deduplicates published training by event ID',()=>{
  const plan={id:'a',day:'2026-11-10',start_time:'17:10',title:'Passing',location:'Field',status:'draft'};
  const publication={id:'a',event_id:'os-training-a',is_cancelled:false};
  const sessions=[
    {id:'os-training-a',day:plan.day,title:'Passing',is_cancelled:false,is_archived:false},
    {id:'legacy',day:plan.day,title:'Legacy training',is_cancelled:false,is_archived:false},
    {id:'archive',day:plan.day,title:'Archived',is_cancelled:false,is_archived:true}];
  const rows=sortCalendar(calendarTraining(sessions,[plan],[publication]));
  assert.equal(rows.length,2);
  assert.equal(rows[0].state,'published');
  assert.equal(rows[0].time,'17:10');
  assert.equal(rows[1].time,null);
  assert.equal(calendarTraining(sessions,[plan],[{...publication,is_cancelled:true}])[1].state,'cancelled');
});
test('public fixtures require the exact owner and group and a real date',()=>{
  const fixture={id:'one',organization_id:owner,academy_group_id:group,team_id:null,
    match_date:'2026-11-21',match_time:null,home_team:'Home',away_team:'Away',status:'Completed',venue:'Field'};
  const value={organization_id:owner,fixtures:[fixture,{...fixture,id:'two',academy_group_id:'other'}]};
  const rows=calendarFixtures(value,owner,group,from,to);
  assert.equal(rows.length,1);assert.equal(rows[0].state,'completed');assert.equal(rows[0].time,null);
  assert.equal(calendarFixtures({...value,organization_id:'lefteria_fc'},owner,group,from,to),null);
  assert.equal(calendarFixtures({...value,fixtures:[{...fixture,match_date:'2026-11-31'}]},owner,group,from,to),null);
});
test('private drafts are filtered by exact group and canonical catalog scope',()=>{
  const match={id:'draft1',organization_id:owner,scope_id:'scope',academy_group_id:group,status:'draft',
    match_date:'2026-11-11',kickoff_local:'18:00',opponent_id:'opponent',playing_field_id:'field'};
  const value={organization_id:owner,groups:[{id:group,scope_id:'scope'}],
    scopes:[{id:'scope',organization_id:owner}],opponents:[{id:'opponent',scope_id:'scope',organization_id:owner,name:'Opponent'}],
    playing_fields:[{id:'field',scope_id:'other',organization_id:owner,name:'Wrong field'}],
    match_drafts:[match,{...match,id:'draft2',academy_group_id:'other'}]};
  const rows=calendarMatchDrafts(value,owner,group,from,to);
  assert.equal(rows.length,1);assert.equal(rows[0].title,'Opponent');assert.equal(rows[0].location,'');
  assert.equal(rows[0].state,'draft');
});
test('calendar is permission gated and existing sources are independently authorized',()=>{
  const user={role:'manager',organizations:[{id:owner}],permissions:['attendance:read']};
  const access=groupWorkspaceAccess(user,owner,true,true);
  assert.equal(access.calendar,true);assert.equal(access.training,false);assert.equal(access.matches,false);
  assert.equal(groupWorkspaceAccess(user,'lefteria_fc',true,true).calendar,false);
  const read=p=>readFileSync(new URL('../src/lib/admin/'+p,import.meta.url),'utf8');
  const panel=read('AdminGroupCalendar.svelte'),workspace=read('AdminGroupWorkspace.svelte');
  for(const [name,source] of [['calendar',panel],['workspace',workspace]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.ok(panel.includes("canAttendance?read(base+'?'+query)"));
  assert.ok(panel.includes("canTraining?read(base+'plans/?'+query)"));
  assert.ok(panel.includes('if(canDrafts)'));
  assert.ok(panel.includes('validTrainingPublicationsPage'));
  assert.ok(workspace.includes('matchDrafts={access.matchWrite}'));
  assert.ok(workspace.includes('onMonthChanged={value=>month=value}'));
});
