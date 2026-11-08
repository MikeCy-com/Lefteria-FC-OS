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
const read = path => readFileSync(new URL('../src/lib/admin/' + path, import.meta.url), 'utf8');

test('admin categories follow legacy website, CRM, POS, finance and football ownership', () => {
  const all = user(['*:*'], ['lefteria_fc', 'lefteria_fc_academy'], 'super_admin');
  assert.deepEqual(groups(all), ['website', 'crm', 'pos', 'finance', 'football', 'operations']);
  const available = modules(all).filter(module => module.status === 'live');
  assert.deepEqual(available.map(module => [module.id, module.href]), [
    ['club', '/admin/club'], ['news', '/admin/news'], ['products', '/admin/pos'], ['trials', '/admin/trials']
  ]);
  assert.ok(modules(all).filter(module => module.status === 'planned').length > 12);
  for(const item of modules(all)) {
    assert.equal(item.status === 'live', item.href !== undefined, item.id);
    assert.ok(!item.href || ['/admin/club', '/admin/news', '/admin/pos', '/admin/trials'].includes(item.href));
  }
});

test('the dashboard cannot leak first-team, financial, POS or Academy modules across grants', () => {
  assert.deepEqual(adminSectionsFor(user(['*:*'], [])), []);
  assert.deepEqual(groups(user(['news:*', 'gallery:*'], ['lefteria_fc_academy'], 'editor')), ['website']);
  assert.deepEqual(ids(user(['news:*'], ['lefteria_fc_academy'], 'editor')), ['news']);
  assert.deepEqual(ids(user(['settings:read'], ['lefteria_fc_academy'], 'editor')), ['club', 'organization']);
  assert.deepEqual(ids(user(['settings:read'], ['lefteria_fc'], 'editor')), ['club', 'trials', 'organization']);
  assert.deepEqual(groups(user(['shop:read'], ['lefteria_fc'], 'editor')), ['pos']);
  assert.deepEqual(ids(user(['finance:read'], ['lefteria_fc_academy'], 'editor')), ['orders', 'charges', 'reports']);
  assert.ok(!ids(user(['academy:read_assigned'], ['lefteria_fc'], 'coach')).includes('registrations'));
  assert.deepEqual(ids(user(['academy:read_assigned'], ['lefteria_fc_academy'], 'coach')), ['registrations', 'academy']);
  assert.deepEqual(ids(user(['marketing:read'], ['lefteria_fc_academy'], 'marketing')), ['leads', 'campaigns']);
  assert.ok(ids(user(['marketing:read'], ['lefteria_fc'], 'marketing')).includes('trials'));
  assert.equal(ids(user(['training:read_assigned'], ['lefteria_fc'], 'coach')).includes('teams'), false);
});

test('all planned admin modules are visibly disabled, never wired to mutations or unavailable routes', () => {
  const workspace = read('AdminWorkspace.svelte');
  const frame = read('AdminFrame.svelte');
  const source = read('module-catalog.ts');
  assert.match(workspace,/module.status === 'live' && module.id === 'club'/);
  assert.match(workspace,/module.status === 'live' && module.id === 'trials'/);
  assert.match(workspace,/admin-tool-planned/);
  assert.match(workspace,/Under Developments/);
  assert.ok(workspace.includes("data-testid={'admin-section-' + section.id}"));
  assert.match(frame,/adminSectionsFor\(user\)/);
  assert.match(frame,/canReadClub\(user\)/);
  assert.match(frame,/canViewFirstTeamTrials\(user\)/);
  assert.doesNotMatch(source,/fetch\(|\bPOST\b|\bPUT\b|\bDELETE\b/);
  assert.doesNotMatch(workspace,/fetch\(|\bPOST\b|\bPUT\b|\bDELETE\b|\/api\/marketing\/leads/);
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
