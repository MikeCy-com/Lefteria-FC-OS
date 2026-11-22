import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {validHeadingLevel,isPublishedWebsitePage} from '../src/lib/public/website-pages.ts';

const read=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const canvas=read('frontend/src/lib/admin/WebsiteVisualCanvas.svelte');
const publicBlocks=read('frontend/src/lib/public/WebsitePageBlocks.svelte');

const heading=(level)=>({type:'heading',text:'Semantic section',url:null,...(level===undefined?{}:{level})});
const published=(block)=>({
  organization_id:'lefteria_fc',slug:'semantics-demo',published_at:'2026-11-22T12:00:00Z',
  page:{title:'Demo',summary:'',seo_title:'',seo_description:'',menu_parent:'hidden',
    menu_order:0,blocks:[block]}
});

test('legacy heading blocks remain valid H2; H3-H6 are supported',()=>{
  assert.equal(validHeadingLevel(heading()),true);
  assert.equal(validHeadingLevel(heading(null)),true);
  for(const level of [2,3,4,5,6]){
    assert.equal(validHeadingLevel(heading(level)),true);
    assert.equal(isPublishedWebsitePage(published(heading(level)),'lefteria_fc'),true);
  }
  for(const level of [0,1,7,100,'h3',3.1]){
    assert.equal(validHeadingLevel(heading(level)),false);
    assert.equal(isPublishedWebsitePage(published(heading(level)),'lefteria_fc'),false);
  }
  assert.equal(validHeadingLevel({type:'paragraph',text:'Text',url:null,level:3}),false);
});

test('API validates only heading levels H2-H6 and disallows levels on non-headings',()=>{
  assert.match(api,/int\? Level = null/);
  assert.match(api,/Type == "heading" \? Level is null or 2 or 3 or 4 or 5 or 6 : Level is null/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
});

test('public rendering keeps semantic real headings; default remains H2',()=>{
  for(const level of [3,4,5,6]){
    assert.match(publicBlocks,new RegExp('block.level==='+level+'.*<h'+level+'>','s'));
  }
  assert.match(publicBlocks,/<h2>\{block.text\}<\/h2>/);
  assert.match(publicBlocks,/<p class="website-text">\{block.text\}<\/p>/);
  assert.ok(compile(publicBlocks,{filename:'WebsitePageBlocks.svelte',generate:'server'}).js.code.length>100);
});

test('editors choose valid semantic heading levels without changing fixed native slots',()=>{
  assert.match(admin,/data-testid="cms-heading-level"/);
  assert.match(admin,/function setHeadingLevel/);
  assert.match(admin,/!validHeadingLevel\(block\)/);
  assert.match(admin,/onLevel=\{setHeadingLevel\}/);
  assert.match(admin,/!isLockedRegion/);
  assert.match(canvas,/data-testid="canvas-heading-level"/);
  assert.match(canvas,/onLevel\?\:/);
  assert.ok(compile(admin,{filename:'admin/website-pages/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(canvas,{filename:'WebsiteVisualCanvas.svelte',generate:'server'}).js.code.length>100);
});
