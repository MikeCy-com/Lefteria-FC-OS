import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { adminSectionsFor } from '../src/lib/admin/module-catalog.ts';

const read = relative => readFileSync(new URL('../src/' + relative, import.meta.url), 'utf8');
const page = read('routes/admin/news/+page.svelte');
const backend = readFileSync(new URL(
  '../../backend/src/LefteriaFc.Api/PublicContent/AdminNewsEndpoints.cs', import.meta.url), 'utf8');

test('news CMS is a protected real GET, never a public or synthetic CRUD UI', () => {
  assert.ok(page.includes("$auth.status === 'authenticated' && $auth.session"));
  assert.ok(page.includes("hasAdminPermission($auth.session.user.permissions, 'news:read')"));
  assert.ok(page.includes("adminSectionsFor") === false);
  assert.ok(page.includes("credentials: 'same-origin'"));
  assert.ok(page.includes("cache: 'no-store'"));
  assert.ok(page.includes("controller.abort()"));
  assert.ok(page.includes("data-testid=\"admin-news-denied\""));
  assert.ok(page.includes("data-testid=\"admin-news-unavailable\""));
  assert.ok(page.includes("data-testid=\"admin-news-empty\""));
  assert.ok(page.includes("data-testid=\"admin-news-list\""));
  assert.ok(page.includes("verifiedResponse(raw, selected, requestedOffset)"));
  assert.doesNotMatch(page, /<form\b|<textarea\b|\binnerHTML\b|\{@html\}|method:\s*'POST'|method:\s*'PUT'|\/api\/public\/news/);
  assert.ok(backend.includes('.RequireAuthorization(WebAdminServices.Policy)'));
  assert.ok(backend.includes('AdminPermissions.Has(session.User.Role, "news:read")'));
  assert.ok(backend.includes('session.User.Organizations.Any(org => org.Id == organization_id)'));
  assert.ok(backend.includes('content.GetNewsAsync(organization_id, token)'));
  assert.doesNotMatch(backend,/\bMapPost\b|\bMapPut\b|\bMapDelete\b/);
});

test('website news appears only for an assigned news reader in the admin menu', () => {
  const session = (permissions, organizations) => ({
    id:'fixture', username:'fixture', name:'Fixture', role:'editor', permissions,
    organizations:organizations.map(id => ({id,name:id}))
  });
  const get = user => adminSectionsFor(user).flatMap(group => group.modules);
  assert.equal(get(session(['news:*'], ['lefteria_fc']))[0]?.href, '/admin/news');
  assert.equal(get(session(['news:*'], ['lefteria_fc_academy']))[0]?.href, '/admin/news');
  assert.ok(!get(session(['news:*'], [])).length);
  assert.ok(!get(session(['gallery:*'], ['lefteria_fc'])).some(x=>x.id==='news'));
  assert.ok(!get(session(['settings:read'], ['lefteria_fc'])).some(x=>x.id==='news'));
  assert.ok(read('lib/admin/AdminFrame.svelte').includes('href="/admin/news"'));
  assert.ok(read('lib/admin/AdminWorkspace.svelte').includes('href="/admin/news"'));
});
