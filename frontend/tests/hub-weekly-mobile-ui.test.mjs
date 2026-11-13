import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validWeeklyLearningEnrollment,validWeeklyLearningInbox}
  from '../src/lib/mobile/weekly-learning.ts';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const component=read('frontend/src/lib/mobile/components/MobileWeeklyLearningInbox.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const id='11111111-1111-4111-8111-111111111111';
const org='lefteria_fc_academy';
const enrollment={organization_id:org,audience:'parent',
  started_at:'2026-11-13T12:00:00Z',
  next_due_at:'2026-11-20T12:00:00Z',
  delivery_week:2,status:'active'};
const inbox={...enrollment,enrolled:true,deliveries:[
  {id,week:1,content_id:'22222222-2222-4222-8222-222222222222',
    title:'The defender position',body:'Protect your goal and help your teammates.',
    delivered_at:'2026-11-13T12:00:00Z'}
]};

test('new individuals start at their own first week and see validated delivered lessons',()=>{
 assert.equal(validWeeklyLearningEnrollment(enrollment,org,'parent'),true);
 assert.equal(validWeeklyLearningEnrollment({...enrollment,delivery_week:0},org,'parent'),false);
 assert.equal(validWeeklyLearningEnrollment(enrollment,org,'player'),false);
 assert.equal(validWeeklyLearningInbox(inbox,org,'parent'),true);
 assert.equal(validWeeklyLearningInbox({...inbox,deliveries:[]},org,'parent'),true);
 assert.equal(validWeeklyLearningInbox({...inbox,deliveries:[
   {...inbox.deliveries[0],title:''}]},org,'parent'),false);
 assert.equal(validWeeklyLearningInbox({...inbox,deliveries:[
   inbox.deliveries[0],inbox.deliveries[0]]},org,'parent'),false);
 assert.equal(validWeeklyLearningInbox({organization_id:org,audience:'fan_parent',
   enrolled:false,deliveries:[]},org,'fan_parent'),true);
});

test('mobile only uses role-bound controller with identity change checks',()=>{
 assert.match(controller,/async enrollWeeklyLearning/);
 assert.match(controller,/async getWeeklyLearningInbox/);
 assert.match(controller,/role==='fan'&&\(audience==='fan_parent'\|\|audience==='fan_player'\)/);
 assert.match(controller,/state\.user\?\.id!==actorId/);
 assert.match(controller,/revision!==actorRevision/);
 assert.match(controller,/validWeeklyLearningInbox\(result,organization,audience\)/);
 assert.match(controller,/validWeeklyLearningEnrollment\(result,organization,audience\)/);
});

test('member HUB mounts private scheduled inbox without fan role escalation',()=>{
 assert.match(route,/import MobileWeeklyLearningInbox from/);
 assert.match(route,/<MobileWeeklyLearningInbox \{controller\} viewerRole=\{role\}/);
 assert.match(component,/controller\.getWeeklyLearningInbox\(owner,track\)/);
 assert.match(component,/controller\.enrollWeeklyLearning\(owner,track\)/);
 assert.match(component,/viewerRole==='fan'/);
 assert.match(component,/subscription|συνδρομή Payabl/);
 assert.doesNotMatch(component,/{@html|innerHTML|localStorage|sessionStorage/);
});
