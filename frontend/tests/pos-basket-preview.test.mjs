import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const basket=load('frontend/src/lib/admin/AdminPosBasketPreview.svelte');
const pos=load('frontend/src/routes/admin/pos/+page.svelte');
test('POS preview is explicitly not a checkout or payment route',()=>{
  assert.match(basket,/Πρόχειρη παραγγελία/);
  assert.match(basket,/Δεν καταχωρίζεται πώληση/);
  assert.doesNotMatch(basket,/fetch\(|method:\s*['"]POST|method:\s*['"]PUT|localStorage|sessionStorage/);
  assert.match(pos,/AdminPosBasketPreview products=\{catalog\.products\} categories=\{catalog\.categories\}/);
});
test('draft uses catalog pricing, bounds and owner reset',()=>{
  assert.match(basket,/product\.active&&categories\.some\(c=>c\.id===p\.category_id&&c\.active\)/);
  assert.match(basket,/variant\.price_cents\*line\.quantity/);
  assert.match(basket,/line\.quantity>=1&&line\.quantity<=99/);
  assert.match(basket,/lines\.length>=30/);
  assert.match(basket,/current\.quantity\+quantity>99/);
  assert.match(basket,/void organization/);
  assert.match(basket,/lines=\[\];chosenProduct='';chosenVariant=''/);
});
