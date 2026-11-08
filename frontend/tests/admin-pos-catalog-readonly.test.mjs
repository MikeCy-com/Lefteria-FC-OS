import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read = rel => readFileSync(new URL('../src/'+rel, import.meta.url), 'utf8');
const page = read('routes/admin/pos/+page.svelte');
const frame = read('lib/admin/AdminFrame.svelte');
const workspace = read('lib/admin/AdminWorkspace.svelte');
const catalog = read('lib/admin/module-catalog.ts');

test('POS catalog fetch is authenticated, scoped, no-store, and has no mutation or fake sales',()=>{
  assert.ok(page.includes("$auth.status === 'authenticated'"));
  assert.ok(page.includes("hasAdminPermission($auth.session.user.permissions, 'shop:read')"));
  assert.ok(page.includes("credentials:'same-origin'"));
  assert.ok(page.includes("cache:'no-store'"));
  assert.ok(page.includes("'/api/admin/pos/catalog?'"));
  assert.ok(page.includes('isCatalog(body, selected)'));
  assert.ok(page.includes('controller.abort()'));
  for(const state of ['admin-pos-loading','admin-pos-unavailable','admin-pos-denied','admin-pos-empty-products']) {
    assert.ok(page.includes(state),state);
  }
  assert.doesNotMatch(page,/<form\b|<textarea\b|\{@html\}|\binnerHTML\b|method:\s*'POST'|method:\s*'PUT'|\/api\/pos\/sale/);
});

test('POS controls retain real page and prevent planned checkout/tools being clickable',()=>{
  assert.ok(frame.includes('href="/admin/pos"'));
  assert.ok(workspace.includes('href="/admin/pos"'));
  assert.ok(catalog.includes("id: 'products', name: 'Κατάλογος προϊόντων'"));
  assert.ok(catalog.includes("status: 'live', href: '/admin/pos'"));
  for(const id of ['registers','orders']) {
    assert.match(catalog, new RegExp("id: '"+id+"'[^\\n]*status: 'planned'"));
  }
});
