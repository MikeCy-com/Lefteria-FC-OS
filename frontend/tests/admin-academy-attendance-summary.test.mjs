import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {summarizeAcademyAttendance} from '../src/lib/admin/attendance-summary.ts';

const page=readFileSync(new URL('../src/routes/admin/attendance/+page.svelte',import.meta.url),'utf8');
test('RSVP and actual attendance are counted separately, including missing responses',()=>{
  const data=[
    {rsvp:'going',attendance:'absent'},
    {rsvp:'going',attendance:'present'},
    {rsvp:'not_going',attendance:'present'},
    {rsvp:'maybe',attendance:null},
    {rsvp:null,attendance:null},
    {rsvp:'no_response',attendance:'absent'}
  ];
  assert.deepEqual(summarizeAcademyAttendance(data),{
    total:6,responses:{going:2,not_going:1,maybe:1,no_response:2},
    actual:{present:2,absent:2,not_recorded:2}});
  assert.deepEqual(summarizeAcademyAttendance([]),{
    total:0,responses:{going:0,not_going:0,maybe:0,no_response:0},
    actual:{present:0,absent:0,not_recorded:0}});
});
test('summary is computed only on verified private register, without mutating RSVP',()=>{
  assert.match(page,/const summary=\$derived\(register\?summarizeAcademyAttendance\(register\.players\):null\)/);
  assert.match(page,/data-testid="attendance-rsvp-summary"/);
  assert.match(page,/summary\.responses\.going/);
  assert.match(page,/summary\.actual\.present/);
  assert.match(page,/Η δήλωση συμμετοχής δεν υποκαθιστά/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions,'attendance:read'\)/);
  assert.doesNotMatch(page,/mockAttendance|fakeRsvp/);
  assert.ok(compile(page,{filename:'AdminAcademyAttendance.svelte',generate:'server'}).js.code.length>0);
});
