import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {canonicalAboutHistory} from '../src/lib/public/canonical-about-history.ts';
import {CANONICAL_ABOUT_HISTORY_SLUG,isHomepageEditorialSlug,withWebsitePages}
  from '../src/lib/public/website-pages.ts';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const about=read('frontend/src/routes/about/+page.svelte');
const loader=read('frontend/src/routes/about/+page.server.ts');
const row=(owner='lefteria_fc')=>({organization_id:owner,
  slug:CANONICAL_ABOUT_HISTORY_SLUG,published_at:'2026-11-22T00:00:00Z',
  page:{title:'History section',summary:'',seo_title:'',seo_description:'',
    menu_parent:'hidden',menu_order:0,blocks:[
      {type:'heading',text:'Club History',url:null},
      {type:'paragraph',text:'First history paragraph',url:null},
      {type:'paragraph',text:'Second history paragraph',url:null},
      {type:'paragraph',text:'Third history paragraph',url:null}
    ]}});

test('About history accepts only owner-scoped approved heading plus three paragraphs',()=>{
  const fc=row();
  assert.deepEqual(canonicalAboutHistory([fc],'lefteria_fc'),{
    heading:'Club History',paragraphs:[
      'First history paragraph','Second history paragraph','Third history paragraph']});
  assert.equal(canonicalAboutHistory([fc],'lefteria_fc_academy'),null);
  assert.equal(canonicalAboutHistory([row('lefteria_fc_academy')],'lefteria_fc'),null);
  assert.equal(canonicalAboutHistory([{...fc,published_at:null}],'lefteria_fc'),null);
  assert.equal(canonicalAboutHistory([{...fc,page:{...fc.page,menu_parent:'root'}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutHistory([{...fc,page:{...fc.page,blocks:fc.page.blocks.slice(0,3)}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutHistory([{...fc,page:{...fc.page,blocks:[fc.page.blocks[0],{type:'image',text:'x',url:'/x'},...fc.page.blocks.slice(2)]}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutHistory([{...fc,page:{...fc.page,blocks:[{...fc.page.blocks[0],text:'Unexpected\nnewline'},...fc.page.blocks.slice(1)]}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutHistory([{...fc,page:{...fc.page,blocks:[{...fc.page.blocks[0],url:'/danger'},...fc.page.blocks.slice(1)]}}],'lefteria_fc'),null);
});

test('history is a hidden non-routable native editorial region with API enforcement',()=>{
  assert.equal(isHomepageEditorialSlug(CANONICAL_ABOUT_HISTORY_SLUG),true);
  assert.deepEqual(withWebsitePages([], [row()], 'lefteria_fc'),[]);
  assert.match(api,/ValidateCanonicalAboutHistory\(body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateCanonicalAboutHistory\(slug, body\.Page\)/);
  assert.match(api,/content\.Blocks\.Count != 4/);
  assert.match(loader,/canonicalAboutHistory\(pages,site\.organization\)/);
});

test('native About keeps crest, values and venue and edits only the history section',()=>{
  assert.match(about,/data\.aboutHistory\.paragraphs/);
  assert.match(about,/data\.aboutHistory\?\.heading/);
  assert.match(about,/data\.site\?\.club\?\.logo_url/);
  assert.match(about,/Οι Αξιες μας/);
  assert.match(about,/Γηπεδο Αετου/);
  assert.match(admin,/data-testid="edit-canonical-about-history"/);
  assert.match(admin,/expected_version:selected\.version,publish/);
  assert.ok(compile(about,{filename:'about/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(admin,{filename:'admin/website-pages/+page.svelte',generate:'server'}).js.code.length>100);
});
