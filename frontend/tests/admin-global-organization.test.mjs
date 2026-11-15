import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import {
  adminOrganizationsFor, preferredAdminOrganization, scopedAdminOrganization
} from '../src/lib/admin/admin-organization.ts';
import { verifiedAdminAudiences, verifiedAdminPublishedNews, verifiedAdminNewLeads } from '../src/lib/admin/admin-overview-data.ts';
import { adminSectionsFor } from '../src/lib/admin/module-catalog.ts';

const club = 'lefteria_fc', academy = 'lefteria_fc_academy';
const admin = {
  id: 'root-1', role: 'super_admin', name: 'Admin', username: 'admin',
  organizations: [{id:club,name:'Lefteria FC'},{id:academy,name:'Academy'}],
  permissions: ['*:*']
};
const page = route => readFileSync(new URL('../src/routes/admin/' + route + '/+page.svelte',import.meta.url),'utf8');

test('all authorized organizations are visible with no browser-persisted global selector', () => {
  assert.deepEqual(adminOrganizationsFor(admin), [club, academy]);
  assert.equal(preferredAdminOrganization(admin),club);
  assert.equal(scopedAdminOrganization(admin,[academy]),academy);
  assert.equal(scopedAdminOrganization(admin,[club]),club);
  assert.deepEqual(adminOrganizationsFor({...admin,organizations:[{id:academy,name:'Academy'}]}),[academy]);
  assert.deepEqual(adminOrganizationsFor({...admin,organizations:[]}),[]);
  assert.equal(preferredAdminOrganization({...admin,organizations:[]}),null);
  assert.equal(scopedAdminOrganization(admin,['tampered-org']),null);
  const helper=readFileSync(new URL('../src/lib/admin/admin-organization.ts',import.meta.url),'utf8');
  const frame=readFileSync(new URL('../src/lib/admin/AdminFrame.svelte',import.meta.url),'utf8');
  assert.doesNotMatch(helper,/localStorage|setItem|chooseAdminOrganization/);
  assert.doesNotMatch(frame,/admin-header-organization|switchOrganization|preferredAdminOrganization/);
  assert.match(frame,/adminSectionsFor\(user\)/);
});

test('Super Admin sees all granted areas while Academy Manager sees Academy-only features', () => {
  const all = adminSectionsFor(admin).flatMap(section => section.modules.map(m => m.id));
  assert.ok(all.includes('trials'));
  assert.ok(!all.includes('academy_schedule'));
  assert.ok(!all.includes('fixtures'));
  assert.ok(!all.includes('evaluations'));
  assert.ok(!all.includes('documents'));
  assert.ok(all.includes('groups'));
  assert.ok(all.includes('registrations'));
  const academyManager = {...admin,role:'manager',
    organizations:[{id:academy,name:'Academy'}],
    permissions:['academy:*','attendance:*','marketing:*','finance:*','news:*']};
  const modules=adminSectionsFor(academyManager).flatMap(section=>section.modules.map(m=>m.id));
  assert.ok(modules.includes('groups'));
  assert.ok(!modules.includes('academy_schedule'));
  assert.ok(modules.includes('registrations'));
  assert.ok(!modules.includes('trials'));
});

test('Marketing is limited to content/marketing, accountant to finance, coach to assignment-only', () => {
  const marketing = {...admin,role:'marketing',
    permissions:['news:*','sponsors:*','gallery:*','marketing:*','social:write']};
  const marketingModules=adminSectionsFor(marketing).flatMap(s=>s.modules.map(m=>m.id));
  for(const item of ['news','sponsors','media','social']) assert.ok(marketingModules.includes(item),item);
  for(const item of ['groups','registrations','finance','payabl','trials','documents','attendance'])
    assert.ok(!marketingModules.includes(item),item);
  const accountant={...admin,role:'accountant',permissions:['finance:read','finance:write']};
  const accountantSections=adminSectionsFor(accountant);
  assert.deepEqual(accountantSections.map(x=>x.id),['finance']);
  const coach={...admin,role:'coach',organizations:[{id:academy,name:'Academy'}],
    permissions:['academy:read_assigned','training:read_assigned','attendance:write_assigned','fixtures:read']};
  const coachModules=adminSectionsFor(coach).flatMap(x=>x.modules.map(m=>m.id));
  assert.ok(coachModules.includes('assigned_groups'));
  assert.ok(!coachModules.includes('groups'));
  assert.ok(!coachModules.includes('attendance'));
  assert.ok(!coachModules.includes('registrations'));
});

test('News content destination is local, permission-scoped and resets on organization change', () => {
  const news=page('news');
  assert.match(news,/data-testid="admin-news-content-target"/);
  assert.match(news,/allowedOrganizations\.some\(org => org.id === next\)/);
  assert.match(news,/organization = next/);
  assert.match(news,/window\.confirm/);
  assert.match(news,/AdminNewsDraftWorkspace owner=\{organization\}/);
  assert.doesNotMatch(news,/header-organization|localStorage/);
});

test('Academy-only and Club-only deep links use role plus grant, not global state',()=>{
  for(const route of ['academy-office','attendance','schedule','leads']) {
    const s=page(route);
    assert.match(s,/lefteria_fc_academy/);
    assert.doesNotMatch(s,/preferredAdminOrganization/);
  }
  const trials=page('trials');
  assert.match(trials,/canViewFirstTeamTrials/);
  assert.doesNotMatch(trials,/preferredAdminOrganization/);
});

test('overview verifies actual numbers without inventing cross-org results', () => {
  assert.deepEqual(verifiedAdminAudiences({
    organization_id:club,ready:true,unassigned_players:2,groups:[
      {id:'first',name:'First Team',kind:'team',active_players:24},
      {id:'selected',name:'Selected',kind:'team',active_players:8}]
  },club),{groupCount:2,activeAssignments:32,unassignedPlayers:2});
  assert.equal(verifiedAdminAudiences({
    organization_id:club,ready:true,unassigned_players:0,
    groups:[{id:'x',name:'X',kind:'team',active_players:3}]},academy),null);
  assert.deepEqual(verifiedAdminPublishedNews({items:[],total:17,offset:0,limit:20},20),{publishedArticles:17});
  assert.equal(verifiedAdminPublishedNews({items:[],total:17,offset:0,limit:100},20),null);
  const good={organization_id:academy,offset:0,limit:25,total:2,
    leads:[{id:'l1',organization_id:academy,status:'new'},
      {id:'l2',organization_id:academy,status:'new'}]};
  assert.equal(verifiedAdminNewLeads(good,academy,25),2);
  assert.equal(verifiedAdminNewLeads(good,club,25),null);
});
