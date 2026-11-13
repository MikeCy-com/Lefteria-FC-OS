import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {ROLE_HOME_SHORTCUTS,trustedRoleHomeEvents} from '../src/lib/mobile/role-home.ts';

const source=readFileSync(new URL('../src/lib/mobile/components/MobileRoleHome.svelte',import.meta.url),'utf8');
const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
const today='2026-11-13';
const event=(id,organization_id='lefteria_fc_academy',date='2026-11-16')=>({
  id,kind:'training',title:'Προπόνηση',organization_id,date,end_date:date,
  is_cancelled:false,start_time:'17:00'
});
const payload=(role='coach')=>({
  identity_id:'coach-1',role,organization_id:'lefteria_fc_academy',
  selected_player_id:null,month:'2026-11',items:[event('b'),event('a')]
});
test('all four missing role homes are mounted under authenticated current role and owner',()=>{
  assert.match(route,/import MobileRoleHome from/);
  assert.match(route,/activeTab === 'home' && role !== 'parent'/);
  assert.match(route,/role==='management'\?\$controller\.organization\?\.organization_id/);
  assert.match(route,/onLoadCalendar=\{month=>controller\.loadCalendar\(month\)\}/);
  for(const role of ['player','coach','management','fan']){
    assert.ok(ROLE_HOME_SHORTCUTS[role].length>0);
  }
  assert.ok(compile(source,{filename:'MobileRoleHome.svelte',generate:'server'}).js.code.length>0);
});
test('calendar items require exactly the same authenticated identity, role and current month',()=>{
  assert.deepEqual(trustedRoleHomeEvents(payload(),'coach','coach-1',null,today).map(x=>x.id),['a','b']);
  assert.deepEqual(trustedRoleHomeEvents(payload(),'player','coach-1',null,today),[]);
  assert.deepEqual(trustedRoleHomeEvents(payload(),'coach','other',null,today),[]);
  assert.deepEqual(trustedRoleHomeEvents({...payload(),month:'2026-10'},'coach','coach-1',null,today),[]);
  assert.deepEqual(trustedRoleHomeEvents({...payload(),selected_player_id:'child'},'coach','coach-1',null,today),[]);
  assert.deepEqual(trustedRoleHomeEvents(payload('management'),'management','coach-1','lefteria_fc',today),[]);
  assert.deepEqual(trustedRoleHomeEvents(payload('management'),'management','coach-1','lefteria_fc_academy',today).map(x=>x.id),['a','b']);
});
test('no stale organization, cancelled or past event can appear in authorized home',()=>{
  const calendar={...payload('player'),items:[event('future'),event('other','lefteria_fc'),
    {...event('cancelled'),is_cancelled:true},event('past','lefteria_fc_academy','2026-11-01')]};
  assert.deepEqual(trustedRoleHomeEvents(calendar,'player','coach-1',null,today).map(x=>x.id),['future']);
  assert.deepEqual(trustedRoleHomeEvents(null,'player','coach-1',null,today),[]);
  assert.match(source,/role==='fan'\|\|!contextIsCurrent/);
  assert.match(source,/needsOrganization/);
  assert.doesNotMatch(source,/localStorage|fetch\(|{@html}|innerHTML|demoFixture|mockData/);
});
