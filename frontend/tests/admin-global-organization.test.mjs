import assert from 'node:assert/strict';
import test from 'node:test';
import {
  adminOrganizationKey, preferredAdminOrganization, scopedAdminOrganization,
  chooseAdminOrganization
} from '../src/lib/admin/admin-organization.ts';
import { verifiedAdminAudiences, verifiedAdminPublishedNews } from '../src/lib/admin/admin-overview-data.ts';
import { adminSectionsFor } from '../src/lib/admin/module-catalog.ts';

const club = 'lefteria_fc', academy = 'lefteria_fc_academy';
const root = {
  id:'root-1', role:'super_admin', name:'Admin', username:'admin',
  organizations:[{id:club,name:'Lefteria FC'},{id:academy,name:'Lefteria FC Academy'}],
  permissions:['*:*']
};
const storage = () => {
  const map = new Map();
  return { getItem(key) { return map.get(key) ?? null; }, setItem(key,value) { map.set(key,value); } };
};

test('super admin gets one reusable organization preference for all web admin pages', () => {
  const store = storage();
  assert.equal(preferredAdminOrganization(root,store), club);
  assert.equal(chooseAdminOrganization(root,academy,store),true);
  assert.equal(store.getItem(adminOrganizationKey(root)),academy);
  assert.equal(preferredAdminOrganization(root,store),academy);
  assert.equal(scopedAdminOrganization(root,[club,academy],store),academy);
  assert.equal(scopedAdminOrganization(root,[club],store),null,'no silent cross-context fallback');
});
test('selection is not a permission: forged and revoked organizations are rejected', () => {
  const store = storage();
  store.setItem(adminOrganizationKey(root),'forged-org');
  assert.equal(preferredAdminOrganization(root,store),club);
  assert.equal(chooseAdminOrganization(root,'forged-org',store),false);
  const onlyAcademy={...root,id:'coach-1',role:'coach',organizations:[{id:academy,name:'Academy'}]};
  assert.equal(chooseAdminOrganization(onlyAcademy,club,store),false);
  assert.equal(preferredAdminOrganization(onlyAcademy,store),academy);
  store.setItem(adminOrganizationKey(root),academy);
  assert.equal(preferredAdminOrganization({...root,organizations:[{id:club,name:'Club'}]},store),club);
  assert.equal(preferredAdminOrganization({...root,organizations:[]},store),null);
});
test('different administrators never share context or gain permissions from local storage', () => {
  const store=storage(), another={...root,id:'root-2'};
  chooseAdminOrganization(root,academy,store);
  assert.equal(preferredAdminOrganization(another,store),club);
  assert.equal(preferredAdminOrganization(root,store),academy);
});
test('module navigation scopes only to the header-selected organization', () => {
  const selectedClub=adminSectionsFor(root,club).flatMap(s=>s.modules.map(m=>m.id));
  const selectedAcademy=adminSectionsFor(root,academy).flatMap(s=>s.modules.map(m=>m.id));
  assert.ok(selectedClub.includes('groups'));
  assert.ok(selectedAcademy.includes('groups'));
  assert.ok(selectedClub.includes('trials'));
  assert.ok(!selectedAcademy.includes('trials'));
  assert.ok(!selectedClub.includes('academy_schedule'));
  assert.ok(selectedAcademy.includes('academy_schedule'));
  assert.ok(!selectedClub.includes('registrations'));
  assert.ok(selectedAcademy.includes('registrations'));
});
test('overview accepts verified counts and never derives fake players or article totals', () => {
  assert.deepEqual(verifiedAdminAudiences({
    organization_id:club,ready:true,unassigned_players:2,groups:[
      {id:'first',name:'First Team',kind:'team',active_players:24},
      {id:'selected',name:'Selected',kind:'team',active_players:8}
    ]
  },club),{groupCount:2,activeAssignments:32,unassignedPlayers:2});
  assert.equal(verifiedAdminAudiences({
    organization_id:club,ready:true,unassigned_players:0,groups:[{id:'x',name:'X',kind:'team',active_players:3}]
  },academy),null);
  assert.equal(verifiedAdminAudiences({
    organization_id:club,ready:true,unassigned_players:0,groups:[{id:'x',name:'X',kind:'team',active_players:-1}]
  },club),null);
  assert.deepEqual(verifiedAdminPublishedNews({items:[],total:17,offset:0,limit:20},20),{publishedArticles:17});
  assert.equal(verifiedAdminPublishedNews({items:[],total:17,offset:0,limit:100},20),null);
});
