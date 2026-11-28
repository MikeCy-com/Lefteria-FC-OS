import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {trainingSeries,trainingSeriesWindows,seriesPlanStatus} from '../src/lib/admin/academy-training-series.ts';
const input={start:'2027-01-04',end:'2027-01-10',title:'Training',location:'Field',notes:'',slots:[
  {weekday:1,start_time:'17:10',duration_minutes:60},
  {weekday:3,start_time:'18:20',duration_minutes:70}
]};
test('recurring sessions have accurate local calendar dates and per-day times',()=>{
  const rows=trainingSeries(input);
  assert.deepEqual(rows?.map(r=>[r.day,r.start_time,r.duration_minutes]),[
    ['2027-01-04','17:10',60],['2027-01-06','18:20',70]
  ]);
  assert.equal(trainingSeries({...input,start:'2027-02-30'}),null);
  assert.equal(trainingSeries({...input,end:'2028-01-10'}),null);
  assert.equal(trainingSeries({...input,slots:[{weekday:1,start_time:'23:30',duration_minutes:60}]}),null);
  assert.equal(trainingSeries({...input,slots:[...input.slots,input.slots[0]]}),null);
});
test('season reads use bounded non-overlapping server windows',()=>{
  const windows=trainingSeriesWindows('2027-01-01','2027-12-31');
  assert.equal(windows.length,4);
  assert.equal(windows[0].start,'2027-01-01');
  assert.equal(windows.at(-1).end,'2027-12-31');
  for(const window of windows)assert.ok((Date.parse(window.end)-Date.parse(window.start))/86400000<=93);
  for(let i=1;i<windows.length;i++)assert.equal(Date.parse(windows[i].start)-Date.parse(windows[i-1].end),86400000);
});
test('identical drafts are skipped and overlapping different sessions block creation',()=>{
  const plan=trainingSeries(input)[0];
  const row={...plan,status:'draft'};
  assert.equal(seriesPlanStatus(plan,[]),'new');
  assert.equal(seriesPlanStatus(plan,[row]),'existing');
  assert.equal(seriesPlanStatus(plan,[{...row,title:'Other'}]),'conflict');
  assert.equal(seriesPlanStatus(plan,[{...row,start_time:'18:10'}]),'new');
  assert.equal(seriesPlanStatus(plan,[{...row,status:'archived'}]),'new');
});
test('group profile, player and coach workflow surfaces compile',()=>{
  for(const name of ['AdminTrainingSeriesPanel','AdminAcademyTrainingPlans','AdminGroupWorkspace','AdminCoachDirectoryPanel','AdminAcademyGroupProfilePanel','AdminFirstTeamProfilePanel','AdminRosterPlayersPanel']){
    const source=readFileSync(new URL('../src/lib/admin/'+name+'.svelte',import.meta.url),'utf8');
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  }
  const series=readFileSync(new URL('../src/lib/admin/AdminTrainingSeriesPanel.svelte',import.meta.url),'utf8');
  assert.ok(series.includes('!confirmed||!preview'));
  assert.ok(series.includes('conflicts.length'));
  assert.ok(series.includes('expected_version:0,archive:false'));
  assert.ok(series.includes('matchingTrainingPlanWrite(raw,scope,undefined,1,'));
  assert.ok(!series.includes('/publish'));
  const coaches=readFileSync(new URL('../src/lib/admin/AdminCoachDirectoryPanel.svelte',import.meta.url),'utf8');
  assert.ok(coaches.includes('previous.filter(id=>id!==focusGroupId)'));
  assert.ok(coaches.includes('desired.filter(id=>id!==focusGroupId)'));
});

test('season exceptions skip only selected scheduled dates, preserving other weekdays and times',()=>{
  const rows=trainingSeries({...input,excludedDays:['2027-01-04']});
  assert.deepEqual(rows?.map(r=>[r.day,r.start_time]),[['2027-01-06','18:20']]);
  for(const excludedDays of [['2027-01-04','2027-01-04'],['2027-01-03'],['2027-01-05'],['2027-02-30'],['2027-01-04','2027-01-06']])
    assert.equal(trainingSeries({...input,excludedDays}),null);
  assert.deepEqual(trainingSeries({...input,excludedDays:[]}),trainingSeries(input));
});
test('exception changes invalidate confirmed preview and cannot cancel existing sessions',()=>{
  const source=readFileSync(new URL('../src/lib/admin/AdminTrainingSeriesPanel.svelte',import.meta.url),'utf8');
  assert.match(source,/data-testid="training-series-exceptions"/);
  assert.ok(source.includes('excludedDays}'));
  assert.ok(source.includes('if(previewKey&&key!==previewKey)'));
  assert.ok(source.includes('if(busy||!permitted||!canExclude)return;'));
  assert.ok(!source.includes("method:'DELETE'"));
});
