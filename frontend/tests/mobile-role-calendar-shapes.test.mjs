import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {isTrustedRoleHomeContext,trustedRoleHomeEvents,trustedNextRoleMatch}
  from '../src/lib/mobile/role-home.ts';
const source=readFileSync(new URL('../src/lib/mobile/components/MobileRoleHome.svelte',import.meta.url),'utf8');
const today='2026-11-13';
const item=(id,org)=>({id,kind:'match',organization_id:org,title:id,date:'2026-11-18',
  end_date:'2026-11-18',is_cancelled:false,start_time:'18:00'});
const base={identity_id:'identity',role:'player',month:'2026-11',
  organization_id:'lefteria_fc_academy',selected_player_id:'linked-player',
  players:[{id:'linked-player',name:'Real player',organization_id:'lefteria_fc_academy'}],
  items:[item('academy-game','lefteria_fc_academy')]};
test('player own-link calendar matches real contract while excluding alien teams',()=>{
  assert.equal(isTrustedRoleHomeContext(base,'player','identity',null,today),true);
  assert.deepEqual(trustedRoleHomeEvents(base,'player','identity',null,today).map(x=>x.id),['academy-game']);
  assert.equal(trustedNextRoleMatch(base,'player','identity',null,today)?.id,'academy-game');
  assert.equal(isTrustedRoleHomeContext({...base,selected_player_id:'other'},'player','identity',null,today),false);
  assert.equal(isTrustedRoleHomeContext({...base,players:[...base.players,...base.players]},'player','identity',null,today),false);
  assert.equal(isTrustedRoleHomeContext({...base,organization_id:'lefteria_fc'},'player','identity',null,today),false);
  assert.deepEqual(trustedRoleHomeEvents({...base,items:[item('foreign','lefteria_fc')]},'player','identity',null,today),[]);
});
test('coach cross-organization calendar matches real null owner and filters invalid owner',()=>{
  const coach={...base,role:'coach',organization_id:null,selected_player_id:null,players:[],
    items:[item('first-team','lefteria_fc'),item('academy','lefteria_fc_academy'),item('unknown','different_org')]};
  assert.equal(isTrustedRoleHomeContext(coach,'coach','identity',null,today),true);
  assert.deepEqual(trustedRoleHomeEvents(coach,'coach','identity',null,today).map(x=>x.id),['academy','first-team']);
  assert.deepEqual(trustedRoleHomeEvents({...coach,organization_id:'lefteria_fc'},'coach','identity',null,today),[]);
  assert.deepEqual(trustedRoleHomeEvents({...coach,selected_player_id:'linked-player'},'coach','identity',null,today),[]);
});
test('management requires current server-selected organization, unlinked player is legitimately empty',()=>{
  const manager={...base,role:'management',selected_player_id:null,players:[]};
  assert.equal(isTrustedRoleHomeContext(manager,'management','identity','lefteria_fc_academy',today),true);
  assert.equal(isTrustedRoleHomeContext(manager,'management','identity','lefteria_fc',today),false);
  assert.equal(isTrustedRoleHomeContext(manager,'management','identity',null,today),false);
  const emptyPlayer={...base,selected_player_id:null,players:[],organization_id:null,items:[]};
  assert.equal(isTrustedRoleHomeContext(emptyPlayer,'player','identity',null,today),true);
  assert.equal(isTrustedRoleHomeContext({...emptyPlayer,items:[item('hidden','lefteria_fc')]},'player','identity',null,today),false);
  assert.match(source,/isTrustedRoleHomeContext\(calendar,role,identityId,organizationId,today\)/);
});
