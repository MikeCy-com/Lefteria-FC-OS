import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const territories=read('src/routes/admin/territories/+page.svelte');
const pos=read('src/routes/admin/pos/+page.svelte');
const records=read('src/lib/admin/AdminPlayerPrivateRecordsPanel.svelte');
test('usability surfaces compile and use verified territory selections',()=>{
  for(const [name,source] of [['territories',territories],['pos',pos],['records',records]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.match(territories,/bind:value=\{cityName\}/);
  assert.match(territories,/bind:value=\{locationName\}/);
  assert.match(territories,/pendingLocationId=createdId/);
  assert.match(territories,/directory\.locations\.find\(item=>item\.id===requestedLocation\)/);
  assert.match(territories,/verifiedLocation\.city_id===cityId/);
  assert.match(territories,/directory\.organization_id!==organization/);
  assert.match(territories,/data-testid="territory-create-group-direct"/);
});
test('POS shortcuts have real destinations and owner choice remains granted',()=>{
  for(const anchor of ['pos-basket','pos-sites','pos-shifts','pos-finance','pos-stock']){
    assert.ok(pos.includes('href="#'+anchor+'"'));
    assert.ok(pos.includes('id="'+anchor+'"'));
  }
  assert.match(pos,/assignments\.some\(item=>item\.id===org\.id\)/);
  assert.match(pos,/\{#key catalog\.organization_id\}/);
  assert.match(pos,/disabled=\{busy\|\|writing\|\|productWriting\|\|showCategoryForm\|\|showProductForm\}/);
});
test('private document and evaluation actions stay isolated and upload retains quarantine',()=>{
  assert.match(records,/if\(showCreate\)showUpload=false/);
  assert.match(records,/if\(showUpload\)showCreate=false/);
  assert.match(records,/admin-document-quarantine-form/);
  assert.match(records,/protectedTypes\.find\(type=>type\.id===doc\.document_type\)/);
});
