import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { adminSectionsFor } from '../src/lib/admin/module-catalog.ts';

const user = (permissions, organizations = ['lefteria_fc'], role = 'manager') => ({
  id: 'fixture', username: 'fixture', name: 'Fixture', role, permissions,
  organizations: organizations.map(id => ({id, name: id}))
});
const modules = session => adminSectionsFor(session).flatMap(section => section.modules);
const ids = session => modules(session).map(module => module.id);
const groups = session => adminSectionsFor(session).map(section => section.id);
const legacyIds = session => ids(session).filter(id => id !== 'onboarding' && !id.startsWith('hub_'));
const read = path => readFileSync(new URL('../src/lib/admin/' + path, import.meta.url), 'utf8');

test('admin categories follow legacy website, CRM, POS, finance and football ownership', () => {
  const all = user(['*:*'], ['lefteria_fc', 'lefteria_fc_academy'], 'super_admin');
  assert.deepEqual(groups(all), ['website', 'hub', 'app', 'crm', 'pos', 'finance', 'football', 'operations']);
  const available = modules(all).filter(module => module.status === 'live');
  assert.deepEqual(available.map(module => [module.id, module.href]), [
    ['pages', '/admin/website-pages'], ['news', '/admin/news'], ['sponsors', '/admin/sponsors'], ['media', '/admin/media'], ['hub_editorial', '/admin/hub'], ['hub_subscriptions', '/admin/hub/subscriptions'], ['communications', '/admin/communications'], ['announcements', '/admin/announcements'], ['leads', '/admin/leads'], ['registrations', '/admin/academy-office'], ['registers', '/admin/pos'], ['products', '/admin/pos'], ['orders', '/admin/pos'], ['payabl', '/admin/finance'], ['trials', '/admin/trials'], ['groups', '/admin/groups'], ['drills', '/admin/drills'], ['territories', '/admin/territories'], ['club', '/admin/club'], ['onboarding', '/admin/onboarding'], ['staff', '/admin/staff'], ['facilities', '/admin/territories'], ['users', '/admin/users']
  ]);
  const planned = modules(all).filter(module => module.status === 'planned');
  assert.ok(planned.length >= 10, 'remaining placeholder modules are explicitly planned');
  assert.ok(!planned.some(module => module.id === 'sponsors'), 'verified sponsor editorial module is no longer a placeholder');
  assert.ok(planned.some(module => module.id === 'social'));
  for(const item of modules(all)) {
    assert.equal(item.status === 'live', item.href !== undefined, item.id);
    assert.ok(!item.href || ['/admin/drills', '/admin/club', '/admin/website-pages', '/admin/news', '/admin/media', '/admin/sponsors', '/admin/hub', '/admin/hub/subscriptions', '/admin/pos', '/admin/trials', '/admin/groups', '/admin/announcements', '/admin/communications', '/admin/onboarding', '/admin/leads', '/admin/finance', '/admin/attendance', '/admin/schedule', '/admin/documents', '/admin/evaluations', '/admin/academy-office', '/admin/matches','/admin/staff','/admin/users','/admin/territories'].includes(item.href));
  }
});

test('communication control centre never appears for unassigned staff or unrelated roles',()=>{
  assert.ok(ids(user(['teams:write'],['lefteria_fc'],'manager')).includes('communications'));
  assert.ok(ids(user(['academy:write'],['lefteria_fc_academy'],'super_admin')).includes('communications'));
  assert.ok(!ids(user(['teams:write'],['lefteria_fc'],'editor')).includes('communications'));
  assert.ok(!ids(user(['messages:read'],['lefteria_fc'],'manager')).includes('communications'));
  assert.ok(!ids(user(['academy:write'],['lefteria_fc'],'manager')).includes('communications'));
  assert.ok(!ids(user(['teams:write'],[],'manager')).includes('communications'));
});

test('the dashboard cannot leak first-team, financial, POS or Academy modules across grants', () => {
  assert.deepEqual(adminSectionsFor(user(['*:*'], [])), []);
  assert.deepEqual(ids(user(['news:read'])).filter(id => id.startsWith('hub_')), ['hub_editorial']);
  assert.deepEqual(ids(user(['finance:read'])).filter(id => id.startsWith('hub_')), []);
  assert.deepEqual(ids(user(['finance:read'], ['lefteria_fc'], 'super_admin')).filter(id => id.startsWith('hub_')), ['hub_subscriptions']);
  assert.ok(!ids(user(['finance:read'], ['lefteria_fc'], 'manager')).includes('hub_subscriptions'));
  assert.ok(!ids(user(['finance:read'], ['lefteria_fc'], 'accountant')).includes('hub_subscriptions'));
  assert.deepEqual(ids(user(['shop:read'])).filter(id => id.startsWith('hub_')), []);
  assert.deepEqual(groups(user(['news:*', 'gallery:*'], ['lefteria_fc_academy'], 'editor')), ['website','hub']);
  assert.deepEqual(legacyIds(user(['news:*'], ['lefteria_fc_academy'], 'editor')), ['pages','news']);
  assert.deepEqual(legacyIds(user(['settings:read'], ['lefteria_fc_academy'], 'editor')), ['club', 'organization']);
  assert.deepEqual(legacyIds(user(['settings:read'], ['lefteria_fc'], 'editor')), ['trials', 'club', 'organization']);
  assert.deepEqual(groups(user(['shop:read'], ['lefteria_fc'], 'editor')), ['pos']);
  assert.deepEqual(legacyIds(user(['finance:read'], ['lefteria_fc_academy'], 'editor')), []);
  assert.ok(!legacyIds(user(['academy:read_assigned'], ['lefteria_fc'], 'coach')).includes('registrations'));
  assert.ok(!ids(user(['academy:read_assigned'], ['lefteria_fc_academy'], 'coach')).includes('assigned_groups'));
  assert.ok(modules(user(['training:read_assigned'], ['lefteria_fc_academy'], 'coach')).some(m=>m.id==='assigned_groups'&&m.status==='live'&&m.href==='/admin/schedule'));
  assert.deepEqual(legacyIds(user(['marketing:read'], ['lefteria_fc_academy'], 'marketing')), ['campaigns']);
  assert.ok(!legacyIds(user(['marketing:read'], ['lefteria_fc'], 'marketing')).includes('trials'));
  assert.equal(legacyIds(user(['training:read_assigned'], ['lefteria_fc'], 'coach')).includes('groups'), false);
});

test('facilities reuses verified territory management and stays closed to unrelated roles',()=>{
  const manager=user(['academy:read'],['lefteria_fc_academy'],'manager');
  const facilities=modules(manager).find(item=>item.id==='facilities');
  assert.equal(facilities?.status,'live');
  assert.equal(facilities?.href,'/admin/territories');
  assert.ok(!ids(user(['academy:read'],['lefteria_fc'],'manager')).includes('facilities'));
  assert.ok(!ids(user(['teams:read'],['lefteria_fc_academy'],'manager')).includes('facilities'));
  assert.ok(!ids(user(['academy:read_assigned'],['lefteria_fc_academy'],'coach')).includes('facilities'));
  assert.ok(!ids(user(['facilities:read'],['lefteria_fc'],'editor')).includes('facilities'));
  assert.ok(!ids(user(['finance:read'],['lefteria_fc'],'accountant')).includes('facilities'));
  assert.ok(!ids(user(['academy:read'],['lefteria_fc_academy'],'coach')).includes('facilities'));
});

test('admin navigation exposes real operational destinations without five orphan global links',()=>{
  const workspace=read('AdminWorkspace.svelte');
  const frame=read('AdminFrame.svelte');
  const source=read('module-catalog.ts');
  assert.match(frame,/adminSectionsFor\(user\)/);
  assert.match(read('module-catalog.ts'),/canReadClub\(user\)/);
  assert.match(frame,/adminSectionsFor\(user\)/);
  assert.match(frame,/canViewFirstTeamTrials\(user\)/);
  assert.match(frame,/href="\/admin\/profile"/);
  assert.match(frame,/href="\/admin\/profile#access"/);
  assert.match(frame,/href="\/admin\/settings"/);
  assert.match(frame,/href="\/admin\/groups"/);
  for(const orphan of ['schedule','attendance','matches','evaluations','documents']){
    assert.doesNotMatch(frame,new RegExp('href="/admin/'+orphan+'"'));
  }
  assert.doesNotMatch(workspace,/data-testid="admin-quick-navigation"/);
  assert.match(workspace,/AdminClubMetrics/);
  assert.doesNotMatch(workspace,/admin-overview-stats|Under Developments|admin-account-grid|admin-tool-planned/);
  assert.doesNotMatch(source,/fetch\(|\bPOST\b|\bPUT\b|\bDELETE\b/);
  assert.doesNotMatch(workspace,/fetch\(|\bPOST\b|\bPUT\b|\bDELETE\b/);
});

test('admin font scale and high-density sizing match the original parent dashboard', () => {
  const admin = read('admin.css');
  const parent = readFileSync(new URL('../src/lib/mobile/components/mobile-theme.css', import.meta.url), 'utf8');
  for (const [name, pixels] of Object.entries({display:26,title:16,card:14,body:13,label:12,caption:10})) {
    assert.ok(parent.includes(`--lf-type-${name}: ${pixels}px`));
    assert.ok(admin.includes(`--admin-type-${name}:${pixels}px`));
  }
  assert.match(admin,/font-size:clamp\(32px,4vw,43px\)/);
  assert.match(admin,/@media\(min-width:2560px\)\{[\s\S]*?\.admin-app h1\{font-size:43px\}/);
  assert.match(admin,/\.admin-app h2\{font-size:21px/);
  assert.match(admin,/\.admin-app \.admin-tool-copy strong\{[^}]*font-size:19px/);
  assert.match(admin,/\.admin-app \.admin-hub-grid\{display:grid/);
  assert.match(admin,/@media\(max-width:720px\)/);
});

test('one canonical live groups module removes duplicate team/Academy admin links',()=>{
  const manager=user(['*:*'],['lefteria_fc','lefteria_fc_academy'],'super_admin');
  assert.equal(ids(manager).filter(id=>id==='groups').length,1);
  assert.ok(!ids(manager).includes('teams'));
  assert.ok(!ids(manager).includes('academy'));
  assert.ok(ids(user(['teams:read'],['lefteria_fc'])).includes('groups'));
  assert.ok(ids(user(['academy:read'],['lefteria_fc_academy'])).includes('groups'));
  assert.ok(!ids(user(['teams:read'],['lefteria_fc_academy'])).includes('groups'));
  assert.ok(!ids(user(['academy:read'],['lefteria_fc'])).includes('groups'));
  assert.ok(!ids(user(['academy:read_assigned'],['lefteria_fc_academy'],'coach')).includes('groups'));
});

test('training and attendance are contextual group workflows, never global modules',()=>{
  for(const role of ['manager','super_admin','coach']){
    const actor=user(['*:*'],['lefteria_fc_academy'],role);
    for(const id of ['academy_schedule','attendance','documents','evaluations','fixtures'])
      assert.ok(!ids(actor).includes(id),id+' is not a top-level module');
  }
  const groupWorkspace=read('AdminGroupWorkspace.svelte');
  assert.match(groupWorkspace,/AdminAcademyTrainingPlans/);
  assert.match(groupWorkspace,/AdminGroupAttendancePanel/);
  assert.match(groupWorkspace,/AdminGroupPublishedFixtures/);
  assert.match(groupWorkspace,/AdminGroupMatchDraftPanel/);
  assert.match(groupWorkspace,/AdminRosterPlayersPanel/);
});

test('announcement drafting is visible only to authorized owners and managers',()=>{
  assert.ok(ids(user(['teams:write'],['lefteria_fc'],'manager')).includes('announcements'));
  assert.ok(ids(user(['academy:write'],['lefteria_fc_academy'],'manager')).includes('announcements'));
  assert.ok(!ids(user(['teams:write'],['lefteria_fc'],'coach')).includes('announcements'));
  assert.ok(!ids(user(['teams:read'],['lefteria_fc'],'manager')).includes('announcements'));
});


test('application communications are isolated from the public website and CRM',()=>{
  const sections=adminSectionsFor(user(['*:*'],['lefteria_fc','lefteria_fc_academy'],'super_admin'));
  const app=sections.find(s=>s.id==='app');
  const website=sections.find(s=>s.id==='website');
  const crm=sections.find(s=>s.id==='crm');
  assert.ok(app && website && crm);
  assert.ok(app.modules.some(m=>m.id==='announcements' && m.href==='/admin/announcements'));
  assert.ok(app.modules.some(m=>m.id==='messages' && m.status==='planned' && !m.href));
  assert.ok(!website.modules.some(m=>m.id==='announcements' || m.id==='messages'));
  assert.ok(!crm.modules.some(m=>m.id==='announcements' || m.id==='messages'));
  assert.ok(!ids(user(['academy:read_assigned'],['lefteria_fc_academy'],'coach')).includes('announcements'));
});

test('live workflows are reachable in contextual groups and authorized standalone routes',()=>{
  const workspace=read('AdminWorkspace.svelte');
  const frame=read('AdminFrame.svelte');
  assert.doesNotMatch(workspace,/\{#each actions as item/);
  const all=modules(user(['*:*'],['lefteria_fc','lefteria_fc_academy'],'super_admin'));
  for(const id of ['leads','registrations','staff','users']){
    const entry=all.find(m=>m.id===id);
    assert.equal(entry?.status,'live',id);
    assert.ok(entry?.href,id);
    assert.ok(readFileSync(new URL('../src/routes'+entry.href+'/+page.svelte',import.meta.url),'utf8').includes('ADMIN_AUTH_CONTEXT'),id);
  }
  // Old bookmarks still resolve to guarded routes. They are no longer
  // advertised as standalone modules in the administration information architecture.
  for(const href of ['/admin/schedule','/admin/attendance','/admin/evaluations','/admin/documents','/admin/matches'])
    assert.match(readFileSync(new URL('../src/routes'+href+'/+page.svelte',import.meta.url),'utf8'),/ADMIN_AUTH_CONTEXT/);
  for(const href of ['/admin/hub','/admin/academy-office','/admin/groups'])
    assert.ok(frame.includes('href="'+href+'"'),href);
  const settings=readFileSync(new URL('../src/routes/admin/settings/+page.svelte',import.meta.url),'utf8');
  assert.match(frame,/href="\/admin\/settings"/);
  assert.match(settings,/href="\/admin\/staff"/);
  assert.match(settings,/href="\/admin\/users"/);
  assert.match(settings,/href="\/admin\/onboarding"/);
  const groupsPage=readFileSync(new URL('../src/routes/admin/groups/+page.svelte',import.meta.url),'utf8');
  const groupWorkspace=read('AdminGroupWorkspace.svelte');
  assert.match(groupsPage,/AdminGroupWorkspace/);
  assert.match(groupWorkspace,/admin-group-training-link/);
  assert.match(groupWorkspace,/admin-group-attendance-link/);
  assert.match(groupWorkspace,/admin-group-matches-link/);
  assert.match(groupWorkspace,/admin-group-players-link/);
});

test('Academy office discovery matches its existing manager and organization policy',()=>{
  for(const role of ['manager','super_admin'])
    assert.ok(ids(user([],['lefteria_fc_academy'],role)).includes('registrations'));
  for(const role of ['coach','editor','marketing','staff'])
    assert.ok(!ids(user(['*:*'],['lefteria_fc_academy'],role)).includes('registrations'),role);
  assert.ok(!ids(user(['*:*'],['lefteria_fc'],'super_admin')).includes('registrations'));
  assert.ok(!ids(user(['*:*'],[],'super_admin')).includes('registrations'));
});


test('matches are handled only inside granted groups, never as standalone global modules',()=>{
  const clubManager=user(['teams:read'],['lefteria_fc'],'manager');
  const academyManager=user(['academy:read'],['lefteria_fc_academy'],'manager');
  for(const actor of [clubManager,academyManager]){
    assert.ok(ids(actor).includes('groups'));
    assert.ok(!ids(actor).includes('fixtures'));
  }
  assert.ok(!ids(user(['teams:read'],['lefteria_fc_academy'])).includes('groups'));
  assert.ok(!ids(user(['academy:read'],['lefteria_fc'])).includes('groups'));
  assert.match(read('AdminGroupWorkspace.svelte'),/AdminGroupPublishedFixtures/);
});

test('coach staff module requires organization-specific write grants and manager role',()=>{
  const allowed=(permissions,owners,role='manager')=>
    modules(user(permissions,owners,role)).find(x=>x.id==='staff');
  assert.equal(allowed(['teams:write'],['lefteria_fc'])?.href,'/admin/staff');
  assert.equal(allowed(['academy:write'],['lefteria_fc_academy'])?.href,'/admin/staff');
  assert.equal(allowed(['teams:read'],['lefteria_fc']),undefined);
  assert.equal(allowed(['staff:read'],['lefteria_fc']),undefined);
  assert.equal(allowed(['academy:write'],['lefteria_fc']),undefined);
  assert.equal(allowed(['teams:write'],['lefteria_fc_academy']),undefined);
  assert.equal(allowed(['*:*'],['lefteria_fc'],'coach'),undefined);
  assert.equal(allowed(['*:*'],['lefteria_fc'],'editor'),undefined);
  assert.equal(allowed(['*:*'],['lefteria_fc'],'accountant'),undefined);
  const route=readFileSync(new URL('../src/routes/admin/staff/+page.svelte',import.meta.url),'utf8');
  assert.match(route,/adminOrganizationsFor\(\$auth\.session\.user\)/);
  assert.match(route,/AdminCoachDirectoryPanel/);
  assert.match(route,/hasAdminPermission/);
  assert.match(route,/admin-staff-organization/);
  assert.match(route,/validDirectory\(raw,activeOwner\)/);
  assert.doesNotMatch(route,/localStorage|sessionStorage|preferredAdminOrganization|{@html|innerHTML/);
});

test('account directory is discoverable only for verified super admins',()=>{
  const get=(role,permissions,organizations=['lefteria_fc'])=>
    modules(user(permissions,organizations,role)).find(x=>x.id==='users');
  assert.equal(get('super_admin',['*:*'])?.href,'/admin/users');
  assert.equal(get('manager',['*:*']),undefined);
  assert.equal(get('editor',['users:read']),undefined);
  assert.equal(get('accountant',['*:*']),undefined);
  assert.equal(get('coach',['*:*']),undefined);
  assert.equal(get('super_admin',['*:*'],[]),undefined);
});

test('POS site sales history requires manager or Super Admin with BOTH shop and finance permission',()=>{
  assert.ok(ids(user(['shop:read','finance:read'],['lefteria_fc_academy'],'manager')).includes('orders'));
  assert.ok(ids(user(['shop:read','finance:read'],['lefteria_fc'],'super_admin')).includes('orders'));
  assert.ok(!ids(user(['shop:read'],['lefteria_fc_academy'],'manager')).includes('orders'));
  assert.ok(!ids(user(['shop:read','finance:read'],['lefteria_fc_academy'],'editor')).includes('orders'));
  assert.ok(!ids(user(['finance:read'],['lefteria_fc_academy'],'manager')).includes('payabl'));
  assert.ok(ids(user(['finance:read'],['lefteria_fc_academy'],'accountant')).includes('payabl'));
});
