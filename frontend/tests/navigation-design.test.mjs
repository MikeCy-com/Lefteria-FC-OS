import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {NAVIGATION_DESIGN_SLUG,parseNavigationDesign,publishedNavigationDesign,
  withNavigationDesign} from '../src/lib/public/navigation-design.ts';
import {publicNavigation} from '../src/lib/public/navigation.ts';
import {isHomepageEditorialSlug,withWebsitePages} from '../src/lib/public/website-pages.ts';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const editor=read('frontend/src/lib/admin/WebsiteNavigationEditor.svelte');
const shell=read('frontend/src/lib/public/PublicNavigation.svelte');
const config={links:[
  {id:'custom-test-1',label:'Ιστορία',path:'/about',parent:'club',order:0},
  {id:'custom-test-2',label:'Προπονήσεις',path:'/team?tab=schedule',parent:'root',order:3}
]};
const page=(owner='lefteria_fc')=>({organization_id:owner,slug:NAVIGATION_DESIGN_SLUG,
  published_at:'2026-11-22T00:00:00Z',page:{title:'Navigation',summary:'',seo_title:'',
  seo_description:'',menu_parent:'hidden',menu_order:0,
  blocks:[{type:'paragraph',text:JSON.stringify(config),url:null}]}});
test('validated menu entries insert without deleting original functional destinations',()=>{
  assert.deepEqual(parseNavigationDesign(JSON.stringify(config),'lefteria_fc'),config);
  const nav=withNavigationDesign(publicNavigation,config);
  assert.equal(nav.length,publicNavigation.length+1);
  const club=nav.find(item=>item.id==='club');
  assert.ok(club?.items?.some(item=>item[0]==='custom-test-1'));
  for(const id of ['home','team','academy','matches','news','events','shop','contact'])
    assert.ok(nav.some(item=>item.id===id),id);
  assert.equal(withNavigationDesign(publicNavigation,null).length,publicNavigation.length);
});
test('owner boundaries and private routes fail closed',()=>{
  assert.equal(parseNavigationDesign(JSON.stringify(config),'lefteria_fc_academy'),null);
  for(const path of ['/admin','/api/public','/app','//evil','/login','javascript:alert(1)','/academy','/team?tab=roster#x']){
    const copy=structuredClone(config);
    copy.links[0].path=path;
    assert.equal(parseNavigationDesign(JSON.stringify(copy),'lefteria_fc'),null,path);
  }
  const dup={links:[config.links[0],config.links[0]]};
  assert.equal(parseNavigationDesign(JSON.stringify(dup),'lefteria_fc'),null);
  const academy={links:[{id:'custom-academy-1',label:'Academy News',
    path:'/news?organization_id=lefteria_fc_academy',parent:'academy',order:0}]};
  assert.ok(parseNavigationDesign(JSON.stringify(academy),'lefteria_fc_academy'));
  assert.equal(parseNavigationDesign(JSON.stringify({links:[{...academy.links[0],
    path:'/news'}]}),'lefteria_fc_academy'),null);
  assert.equal(parseNavigationDesign(JSON.stringify({links:Array(25).fill(config.links[0])}),'lefteria_fc'),null);
});
test('navigation configuration is a published-only hidden owner-specific CMS record',()=>{
  const fc=page();
  assert.deepEqual(publishedNavigationDesign([fc],'lefteria_fc'),config);
  assert.equal(publishedNavigationDesign([fc],'lefteria_fc_academy'),null);
  assert.equal(publishedNavigationDesign([{...fc,published_at:null}],'lefteria_fc'),null);
  assert.equal(isHomepageEditorialSlug(NAVIGATION_DESIGN_SLUG),true);
  assert.deepEqual(withWebsitePages(publicNavigation,[fc],'lefteria_fc'),[...publicNavigation]);
  assert.match(shell,/publishedNavigationDesign\(publishedPages,organization\)/);
});
test('admin menu editor persists a private validated draft with separate publication',()=>{
  assert.match(api,/ValidateNavigationDesign\(owner, body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateNavigationDesign\(owner, slug, body\.Page\)/);
  assert.match(api,/page\.MenuParent != "hidden"/);
  assert.match(admin,/data-testid="edit-navigation-design"/);
  assert.match(admin,/parseNavigationDesign\(draft\.blocks\[0\]\.text,organization\)/);
  assert.match(admin,/expected_version:selected\?\.version,page:draft/);
  assert.match(admin,/expected_version:selected\.version,publish/);
  assert.match(editor,/data-testid="cms-menu-editor"/);
  assert.ok(compile(admin,{filename:'admin/website-pages/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(editor,{filename:'WebsiteNavigationEditor.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(shell,{filename:'PublicNavigation.svelte',generate:'server'}).js.code.length>100);
});
