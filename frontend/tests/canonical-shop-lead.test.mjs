import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {canonicalShopLead} from '../src/lib/public/canonical-shop.ts';
import {CANONICAL_SHOP_LEAD_SLUG,isHomepageEditorialSlug,withWebsitePages} from '../src/lib/public/website-pages.ts';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const shop=read('frontend/src/routes/shop/+page.svelte');
const loader=read('frontend/src/routes/shop/+page.server.ts');
const row=(organization_id='lefteria_fc')=>({organization_id,slug:CANONICAL_SHOP_LEAD_SLUG,
  published_at:'2026-11-22T00:00:00Z',page:{title:'Shop',summary:'',seo_title:'',
  seo_description:'',menu_parent:'hidden',menu_order:0,
  blocks:[{type:'paragraph',text:'Published first-team shop copy',url:null}]}});
test('shop introduction is FC-owned, published and plain text only',()=>{
  assert.equal(canonicalShopLead([row()]),'Published first-team shop copy');
  assert.equal(canonicalShopLead([row('lefteria_fc_academy')]),null);
  assert.equal(canonicalShopLead([{...row(),published_at:null}]),null);
  assert.equal(canonicalShopLead([{...row(),page:{...row().page,menu_parent:'root'}}]),null);
  assert.equal(canonicalShopLead([{...row(),page:{...row().page,blocks:[{type:'image',text:'x',url:'/x'}]}}]),null);
  assert.equal(canonicalShopLead([{...row(),page:{...row().page,blocks:[{type:'paragraph',text:'x',url:'/admin'}]}}]),null);
});
test('shop editable introduction cannot create a new CMS route, submenu or alter catalog',()=>{
  assert.equal(isHomepageEditorialSlug(CANONICAL_SHOP_LEAD_SLUG),true);
  assert.deepEqual(withWebsitePages([], [row()], 'lefteria_fc'),[]);
  assert.match(api,/ValidateShopEditorialLead\(owner, body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateShopEditorialLead\(owner, slug, body\.Page\)/);
  assert.match(api,/owner != "lefteria_fc"/);
  assert.match(loader,/loadPublicShop\(url\)/);
  assert.match(loader,/canonicalShopLead\(pages\)/);
  assert.match(shop,/data\.editorialLead \?\?/);
  for(const marker of ['data.catalogAvailable','data.products','data.tickets','shop-checkout-status'])assert.ok(shop.includes(marker));
  assert.ok(compile(shop,{filename:'shop/+page.svelte',generate:'server'}).js.code.length>100);
});
test('shop introduction edit is permission-checked, private and versioned',()=>{
  assert.match(admin,/data-testid="edit-canonical-shop-lead"/);
  assert.match(admin,/organization!=='lefteria_fc'/);
  assert.match(admin,/isAboutHistory \|\| isAcademyLead \|\| isShopLead/);
  assert.match(admin,/expected_version:selected\?\.version,page:draft/);
  assert.match(admin,/expected_version:selected\.version,publish/);
});
