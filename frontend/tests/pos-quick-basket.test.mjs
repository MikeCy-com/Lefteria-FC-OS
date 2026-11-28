import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {cashierBasketState,filterCashierStock} from '../src/lib/admin/pos-quick-basket.ts';
const shirt={product_id:'shirt',variant_id:'m',product_name:'Φανέλα',variant_name:'M',price_cents:2500,available:4};
const socks={product_id:'socks',variant_id:'l',product_name:'Κάλτσες',variant_name:'L',price_cents:500,available:0};
test('catalog filtering is local, accent insensitive and does not change basket',()=>{
  assert.deepEqual(filterCashierStock([shirt,socks],'φανελα',true),[shirt]);
  assert.deepEqual(filterCashierStock([shirt,socks],'καλτσες',true),[]);
  assert.deepEqual(filterCashierStock([shirt,socks],'καλτσες',false),[socks]);
});
test('stale or absent stock blocks the whole basket instead of silently omitting lines',()=>{
  assert.equal(cashierBasketState([shirt],{'shirt:m':2}).estimate,5000);
  assert.equal(cashierBasketState([shirt],{'shirt:m':2}).valid,true);
  for(const selected of [{'shirt:m':5},{'shirt:m':2,'socks:l':1},{'shirt:m':2,'missing:x':1}]){
    const state=cashierBasketState([shirt,socks],selected);
    assert.equal(state.valid,false);assert.equal(state.invalidKeys.length,1);
  }
  assert.equal(cashierBasketState([shirt],{'shirt:m':0}).valid,false);
});
test('checkout compiles and keeps price authority, fiscal gate and exact cash confirmation',()=>{
  const panel=readFileSync(new URL('../src/lib/admin/AdminPosCashierCheckoutPanel.svelte',import.meta.url),'utf8');
  assert.doesNotThrow(()=>compile(panel,{filename:'AdminPosCashierCheckoutPanel.svelte',generate:'server'}));
  for(const marker of ['pos-quick-product-search','pos-quick-basket','basketState.valid','removeInvalidSelections','oninput={()=>cashConfirmed=false}','pos-change-preview','request_id:prepId,shift_id:shiftAtStart,items:basket','tenderFrozen=true'])assert.ok(panel.includes(marker),marker);
});
