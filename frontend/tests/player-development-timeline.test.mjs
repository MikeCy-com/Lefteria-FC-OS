import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {buildPlayerDevelopmentTimeline} from '../src/lib/admin/player-development-timeline.ts';

const uuid=(digit)=>digit.repeat(8)+'-'+digit.repeat(4)+'-'+digit.repeat(4)+'-'+digit.repeat(4)+'-'+digit.repeat(12);
const aid=uuid('a'),gid=uuid('b');
const assessments=[{id:aid,summary:'Passing',created_at:'2026-10-03T09:00:00Z'}];
const goals=[{id:gid,title:'First touch',history_count:3,history:[
  {version:3,event:'completed',progress:100,note:'Checked in person',recorded_at:'2026-10-05T16:00:00Z'},
  {version:2,event:'progress',progress:50,note:'Coach review',recorded_at:'2026-10-04T16:00:00Z'},
  {version:1,event:'created',progress:0,note:'',recorded_at:'2026-10-02T14:00:00Z'}
]}];
test('chronological private assessment and goal events link back to the canonical cards',()=>{
  const before=JSON.stringify({assessments,goals});
  const result=buildPlayerDevelopmentTimeline(assessments,goals);
  assert.deepEqual(result.events.map(e=>e.key),[
    'goal:'+gid+':3','goal:'+gid+':2','assessment:'+aid,'goal:'+gid+':1'
  ]);
  assert.equal(result.events[0].targetId,'development-goal-'+gid);
  assert.equal(result.events[2].targetId,'academy-evaluation-'+aid);
  assert.match(result.events[0].description,/100%.*Checked in person/);
  assert.deepEqual([result.loadedCount,result.hiddenOlderCount,result.unreturnedGoalEvents],[4,0,0]);
  assert.equal(JSON.stringify({assessments,goals}),before,'source records remain unchanged');
});
test('bounded timeline reports both UI pagination and server-truncated goal history',()=>{
  const input=[{...goals[0],history_count:53}];
  const result=buildPlayerDevelopmentTimeline(assessments,input,2);
  assert.equal(result.events.length,2);
  assert.equal(result.loadedCount,4);
  assert.equal(result.hiddenOlderCount,2);
  assert.equal(result.unreturnedGoalEvents,50);
});
test('invalid/ambiguous dates are ignored, equal timestamps sort deterministically',()=>{
  const inputs=[
    {id:'c',summary:null,created_at:'2026-11-15T10:00:00+02:00'},
    {id:'a',summary:'X',created_at:'2026-11-15T08:00:00Z'},
    {id:'b',summary:'Bad',created_at:'not-a-date'}
  ];
  inputs.push({id:'d',summary:'No timezone',created_at:'2026-11-15T08:00:00'});
  const result=buildPlayerDevelopmentTimeline(inputs,[]);
  assert.deepEqual(result.events.map(e=>e.key),['assessment:a','assessment:c']);
  assert.equal(result.loadedCount,2);
  assert.equal(buildPlayerDevelopmentTimeline(inputs,[],0).events.length,0);
});
test('timeline restores per-player history and redacts assessments from manager view',()=>{
  const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
  const component=load('frontend/src/lib/admin/AdminPlayerDevelopmentGoals.svelte');
  const panel=load('frontend/src/lib/admin/AdminPlayerPrivateRecordsPanel.svelte');
  const group=load('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
  assert.match(component,/buildPlayerDevelopmentTimeline\(readOnly\?\[\]:assessments,goals\)/);
  assert.match(component,/readOnly\?'Χρονολόγιο στόχων'/);
  assert.match(component,/data-access-mode=\{readOnly\?'read-only':'manage'\}/);
  assert.match(component,/admin-player-development-timeline/);
  assert.match(component,/unreturnedGoalEvents/);
  assert.match(panel,/id=\{'academy-evaluation-'\+evaluation\.id\}/);
  assert.match(component,/id=\{'development-goal-'\+goal\.id\}/);
  assert.match(group,/allowGoalManagement=\{user\.role==='super_admin'\}/);
  assert.doesNotMatch(group,/AdminPlayerDevelopmentTimeline/);
});
