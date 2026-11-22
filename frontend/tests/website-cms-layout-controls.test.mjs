import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {DEFAULT_PAGE_LAYOUT,DEFAULT_BLOCK_LAYOUT,validBlockLayout,validPageLayout}
  from '../src/lib/public/website-pages.ts';
import {nativeEditorialBlocks,nativeEditorialPageLayout}
  from '../src/lib/public/native-editorial-regions.ts';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const canvas=read('frontend/src/lib/admin/WebsiteVisualCanvas.svelte');
const blocks=read('frontend/src/lib/public/WebsitePageBlocks.svelte');
const generic=read('frontend/src/routes/pages/[slug]/+page.svelte');
const team=read('frontend/src/routes/team/+page.svelte');
const news=read('frontend/src/routes/news/+page.svelte');
const sponsor=read('frontend/src/lib/public/PublicCatalogList.svelte');
const row=(layout,blockLayout)=>({
  organization_id:'lefteria_fc',slug:'canonical-team-roster-sections',
  published_at:'2026-11-22T00:00:00Z',page:{
    title:'A',summary:'',seo_title:'',seo_description:'',
    menu_parent:'hidden',menu_order:0,layout,
    blocks:[{type:'paragraph',text:'Safe native content',url:null,layout:blockLayout}]
  }
});

test('responsive width, height, margin and spacing constraints are strict and backward compatible',()=>{
  assert.equal(validBlockLayout(null),true);
  assert.equal(validPageLayout(undefined),true);
  assert.equal(DEFAULT_BLOCK_LAYOUT.width,'full');
  assert.equal(DEFAULT_PAGE_LAYOUT.max_width,1280);
  for(const width of ['full','half','third']){
    assert.equal(validBlockLayout({...DEFAULT_BLOCK_LAYOUT,width}),true);
  }
  for(const value of ['45%','1/2','auto','calc(100vw)']){
    assert.equal(validBlockLayout({...DEFAULT_BLOCK_LAYOUT,width:value}),false);
  }
  assert.equal(validBlockLayout({...DEFAULT_BLOCK_LAYOUT,min_height:-1}),false);
  assert.equal(validBlockLayout({...DEFAULT_BLOCK_LAYOUT,margin_top:9000}),false);
  assert.equal(validPageLayout({...DEFAULT_PAGE_LAYOUT,gap:200}),false);
  assert.equal(validPageLayout({...DEFAULT_PAGE_LAYOUT,max_width:50000}),false);
  assert.equal(validPageLayout({...DEFAULT_PAGE_LAYOUT,side_padding:-32}),false);
});
test('tenant approved layout persists alongside blocks without untrusted CSS',()=>{
  const record=row({max_width:1120,gap:32,side_padding:24},
    {width:'half',min_height:240,margin_top:16,margin_bottom:32});
  assert.equal(nativeEditorialBlocks([record],'lefteria_fc','canonical-team-roster-sections').length,1);
  assert.deepEqual(nativeEditorialPageLayout([record],'lefteria_fc','canonical-team-roster-sections'),record.page.layout);
  assert.equal(nativeEditorialPageLayout([record],'lefteria_fc_academy','canonical-team-roster-sections'),null);
  assert.deepEqual(nativeEditorialBlocks([row({...record.page.layout,max_width:99999},record.page.blocks[0].layout)],'lefteria_fc','canonical-team-roster-sections'),[]);
  assert.deepEqual(nativeEditorialBlocks([row(record.page.layout,{...record.page.blocks[0].layout,width:'calc(900px)'})],'lefteria_fc','canonical-team-roster-sections'),[]);
  assert.match(api,/record WebsiteBlockLayout/);
  assert.match(api,/record WebsitePageLayout/);
  assert.match(api,/Layout is null \|\| Layout\.Valid\(\)/);
  assert.match(api,/Layout is not null && !Layout\.Valid\(\)/);
});
test('published grid stacks mobile and honors full half third span in desktop',()=>{
  assert.match(blocks,/grid-template-columns:repeat\(6,minmax\(0,1fr\)\)/);
  assert.match(blocks,/\[data-width="half"\]/);
  assert.match(blocks,/\[data-width="third"\]/);
  assert.match(blocks,/max-width:var\(--cms-max-width\)/);
  assert.match(blocks,/margin-top:var\(--cms-mt\)/);
  assert.match(blocks,/min-height:var\(--cms-min-height\)/);
  assert.match(blocks,/@media\(max-width:767px\)/);
  for(const component of [blocks,canvas,admin]){
    assert.ok(compile(component,{filename:'visual-editor.svelte',generate:'server'}).js.code.length>100);
  }
});
test('editor and canvas expose persisted layout dimensions with guarded values',()=>{
  assert.match(admin,/data-testid="cms-global-layout"/);
  assert.match(canvas,/data-testid="cms-block-layout-controls"/);
  for(const part of ['max_width','side_padding','gap','min_height','margin_top','margin_bottom']){
    assert.ok((admin+canvas).includes(part),part);
  }
  assert.match(canvas,/option value="full">1\/1/);
  assert.match(canvas,/option value="half">1\/2/);
  assert.match(canvas,/option value="third">1\/3/);
  assert.match(admin,/validPageLayout\(draft\.layout\)/);
  assert.match(admin,/validBlockLayout\(block\.layout\)/);
  assert.match(admin,/onLayout=\{setBlockLayout\}/);
  assert.match(generic,/pageLayout=\{content\.layout\}/);
  for(const target of [team,news,sponsor])assert.match(target,/pageLayout=\{data\.editorialLayout\}/);
});
