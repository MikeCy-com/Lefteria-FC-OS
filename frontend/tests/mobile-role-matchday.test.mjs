import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {trustedNextRoleMatch} from '../src/lib/mobile/role-home.ts';

const page=readFileSync(new URL('../src/lib/mobile/components/MobileRoleHome.svelte',import.meta.url),'utf8');
const item=(id,kind='match',organization_id='lefteria_fc_academy',date='2026-11-17')=>
  ({id,kind,title:'Real fixture '+id,organization_id,date,end_date:date,is_cancelled:false,
    start_time:'18:00',location:'Home'});
const month=(role='coach')=>({
  identity_id:'member',role,month:'2026-11',organization_id:'lefteria_fc_academy',
  selected_player_id:null,items:[item('training','training'),item('later','match','lefteria_fc_academy','2026-11-23'),
    item('earlier'),item('foreign','match','lefteria_fc'),{...item('cancel'),is_cancelled:true}]
});
test('game-day preview chooses earliest server-authorized non-cancelled match, not first training',()=>{
  assert.equal(trustedNextRoleMatch(month(),'coach','member',null,'2026-11-13')?.id,'earlier');
  assert.equal(trustedNextRoleMatch(month(),'player','member',null,'2026-11-13'),null);
  assert.equal(trustedNextRoleMatch(month(),'coach','someone-else',null,'2026-11-13'),null);
  assert.equal(trustedNextRoleMatch(month('management'),'management','member','lefteria_fc', '2026-11-13'),null);
  assert.equal(trustedNextRoleMatch(month('management'),'management','member','lefteria_fc_academy','2026-11-13')?.id,'earlier');
  assert.equal(trustedNextRoleMatch({...month(),month:'2026-10'},'coach','member',null,'2026-11-13'),null);
  assert.equal(trustedNextRoleMatch({...month(),selected_player_id:'child'},'coach','member',null,'2026-11-13'),null);
});
test('match-day screen is only an authenticated calendar projection and cannot invent responses',()=>{
  assert.match(page,/data-testid="role-home-matchday"/);
  assert.match(page,/trustedNextRoleMatch\(calendar,role,identityId,organizationId,today\)/);
  assert.match(page,/onNavigate\('calendar'\)/);
  assert.match(page,/if needsOrganization/);
  assert.doesNotMatch(page,/fakeOpponent|mockMatch|sampleLineup|localStorage|fetch\(/);
  assert.ok(compile(page,{filename:'MobileRoleHome.svelte',generate:'server'}).js.code.length>0);
});
