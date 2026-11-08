import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const page = readFileSync(new URL('../src/routes/admin/pos/+page.svelte', import.meta.url), 'utf8');

test('product editor uses only verified owner-scoped admin API and current write authority',()=>{
  assert.match(page,/data-testid="admin-pos-product-editor"/);
  assert.match(page,/data-testid="admin-pos-product-form"/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions, 'shop:write'\)/);
  assert.match(page,/if \(!browser \|\| !canEdit \|\| !owner \|\| !showProductForm/);
  assert.match(page,/method:currentId===null \? 'POST' : 'PUT'/);
  assert.match(page,/credentials:'same-origin',cache:'no-store'/);
  assert.match(page,/'X-Lefteria-Admin':'1'/);
  assert.match(page,/expected_version:productVersion/);
  assert.match(page,/response\.status===409/);
  assert.match(page,/response\.status===401 \|\| response\.status===403/);
  assert.match(page,/verifiedProduct\(result,currentId,name,variants\)/);
  assert.match(page,/showProductForm=false;\s*retry\+\+/);
  assert.doesNotMatch(page,/\binnerHTML\b|\{@html\}|\/api\/pos\/(checkout|sales|payments)/);
});

test('editor preserves currency cents, full variants, safe image URLs, 16-variant maximum and parent scale',()=>{
  assert.match(page,/centsFromEuros\(value: string\)/);
  assert.match(page,/price <= 0/);
  assert.match(page,/variants\.push\(\{id:variant\.id,name:variant\.name,cost_cents:cost,price_cents:price\}\)/);
  assert.match(page,/productVariants\.length > 16/);
  assert.match(page,/productVariants\.length >= 16/);
  assert.match(page,/newVariant\(next\)/);
  assert.match(page,/\/\^https:/);
  assert.match(page,/font-family:'Fira Sans Extra Condensed'/);
  assert.match(page,/\.admin-pos-variant-row\{display:grid/);
  assert.match(page,/@media\(max-width:600px\)/);
  assert.match(page,/Τα ταμεία, οι πωλήσεις και οι πληρωμές παραμένουν ανενεργές/);
});

test('product form selection cannot leak across owner/permission changes',()=>{
  assert.match(page,/const selected = owner;\s*const permission = canEdit/);
  assert.match(page,/showProductForm = false;\s*editingProductId = null;\s*productVersion = null;/);
  assert.match(page,/owner !== selectedOwner \|\| \$auth\.session\?\.user\.id !== userId \|\| !canEdit/);
  assert.match(page,/!catalog\.categories\.some\(c => c\.id === selectedCategory && c\.active\)/);
});
