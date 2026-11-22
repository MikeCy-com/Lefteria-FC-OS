import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {canonicalAboutIntro} from '../src/lib/public/canonical-about.ts';
import {CANONICAL_ABOUT_INTRO_SLUG,isHomepageEditorialSlug,withWebsitePages}
  from '../src/lib/public/website-pages.ts';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const editor=read('frontend/src/routes/admin/website-pages/+page.svelte');
const publicAbout=read('frontend/src/routes/about/+page.svelte');
const publicLoader=read('frontend/src/routes/about/+page.server.ts');
const canonicalRoute=read('frontend/src/routes/pages/[slug]/+page.server.ts');
const page=(owner='lefteria_fc')=>({organization_id:owner,slug:CANONICAL_ABOUT_INTRO_SLUG,
  published_at:'2026-11-22T00:00:00Z',page:{title:'About intro',summary:'',seo_title:'',
  seo_description:'',menu_parent:'hidden',menu_order:0,blocks:[
    {type:'heading',text:'Our Club',url:null},
    {type:'paragraph',text:'Verified public editorial copy',url:null}]}});

test('approved About intro uses only a scoped published two-block snapshot',()=>{
  const fc=page();
  assert.deepEqual(canonicalAboutIntro([fc],'lefteria_fc'),{
    heading:'Our Club',description:'Verified public editorial copy'});
  assert.equal(canonicalAboutIntro([fc],'lefteria_fc_academy'),null);
  assert.equal(canonicalAboutIntro([{...fc,published_at:null}],'lefteria_fc'),null);
  assert.equal(canonicalAboutIntro([{...fc,page:{...fc.page,menu_parent:'root'}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutIntro([{...fc,page:{...fc.page,blocks:[...fc.page.blocks,{type:'image',text:'x',url:'/x'}]}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutIntro([{...fc,page:{...fc.page,blocks:[{...fc.page.blocks[0],text:'Bad\nheading'},fc.page.blocks[1]]}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutIntro([{...fc,page:{...fc.page,blocks:[{...fc.page.blocks[0],url:'/admin'},fc.page.blocks[1]]}}],'lefteria_fc'),null);
  assert.equal(canonicalAboutIntro([page('lefteria_fc_academy')],'lefteria_fc'),null);
  assert.equal(canonicalAboutIntro([page('lefteria_fc_academy')],'lefteria_fc_academy')?.heading,'Our Club');
});

test('canonical intro remains hidden, never becomes a standalone CMS page or navigation entry',()=>{
  assert.equal(isHomepageEditorialSlug(CANONICAL_ABOUT_INTRO_SLUG),true);
  assert.equal(withWebsitePages([], [page()], 'lefteria_fc').length,0);
  assert.match(canonicalRoute,/isHomepageEditorialSlug/);
  assert.match(api,/ValidateCanonicalAboutIntro\(body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateCanonicalAboutIntro\(slug, body\.Page\)/);
  assert.match(api,/content\.MenuParent != "hidden"/);
  assert.match(api,/content\.Blocks\.Count != 2/);
});

test('the existing About route uses approved text and keeps all native sections and data',()=>{
  assert.match(publicLoader,/readWebsitePages\(site\.organization\)/);
  assert.match(publicLoader,/canonicalAboutIntro\(pages,site\.organization\)/);
  assert.match(publicAbout,/data\.aboutIntro\?\.description \?\?/);
  assert.match(publicAbout,/data\.aboutIntro\.heading/);
  assert.match(publicAbout,/data\.site\?\.club\?\.logo_url/);
  for(const word of ['Οι Αξιες μας','Γηπεδο Αετου','PublicShell'])assert.ok(publicAbout.includes(word));
  assert.ok(compile(publicAbout,{filename:'about/+page.svelte',generate:'server'}).js.code.length>100);
});

test('editor offers an actual About intro draft with optimistic save and manager publication',()=>{
  assert.match(editor,/data-testid="edit-canonical-about-intro"/);
  assert.match(editor,/function openCanonicalAboutIntro/);
  assert.match(editor,/CANONICAL_ABOUT_INTRO_SLUG/);
  assert.match(editor,/isHero \|\| isSpotlight \|\| isAboutIntro \|\| isContactLead \|\| isAboutHistory \|\| isAcademyLead/);
  assert.match(editor,/expected_version:selected\?\.version,page:draft/);
  assert.match(editor,/expected_version:selected\.version,publish/);
  assert.match(editor,/\/about\?organization_id=lefteria_fc_academy/);
  assert.ok(compile(editor,{filename:'admin/website-pages/+page.svelte',generate:'server'}).js.code.length>100);
});
