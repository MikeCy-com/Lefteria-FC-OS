import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {homepageAcademySpotlightPage} from '../src/lib/public/homepage-editorial.ts';
import {HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG,isAcademySpotlightSlug,isHomepageEditorialSlug,
  withWebsitePages} from '../src/lib/public/website-pages.ts';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const academy='lefteria_fc_academy';
const make=(org=academy)=>({
  organization_id:org,
  slug:HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG,
  published_at:'2026-11-15T08:00:00Z',
  page:{title:'Academy Spotlight',summary:'',seo_title:'',seo_description:'',
    menu_parent:'hidden',menu_order:0,blocks:[
      {type:'heading',text:'ΑΝΑΠΤΥΞΗ · ΠΑΙΔΕΙΑ · ΟΝΕΙΡΑ',url:null},
      {type:'paragraph',text:'Κάθε παιδί έχει τη δική του διαδρομή.',url:null}
    ]}
});
test('only an approved Academy-owned spotlight snapshot can change homepage copy',()=>{
  const record=make();
  assert.deepEqual(homepageAcademySpotlightPage([record],academy),record.page);
  assert.equal(homepageAcademySpotlightPage([record],'lefteria_fc'),null);
  assert.equal(homepageAcademySpotlightPage([make('lefteria_fc')],academy),null);
  assert.equal(homepageAcademySpotlightPage([],academy),null);
  assert.equal(homepageAcademySpotlightPage([{...record,published_at:null}],academy),null);
});
test('wrong layout, wrong menu, malformed copy or altered blocks fall back to original static markup',()=>{
  const record=make();
  const withPage=page=>homepageAcademySpotlightPage([{...record,page}],academy);
  const blocks=record.page.blocks;
  for(const invalid of [
    {...record.page,menu_parent:'root'},
    {...record.page,menu_order:1},
    {...record.page,blocks:[...blocks,{type:'button',text:'Open',url:'/academy'}]},
    {...record.page,blocks:[blocks[1],blocks[0]]},
    {...record.page,blocks:[{...blocks[0],text:'NO SEPARATORS'},blocks[1]]},
    {...record.page,blocks:[{...blocks[0],text:'A ·  · C'},blocks[1]]},
    {...record.page,blocks:[{...blocks[0],text:'A · B · C · D'},blocks[1]]},
    {...record.page,blocks:[{...blocks[0],text:'A\nB · C · D'},blocks[1]]},
    {...record.page,blocks:[{...blocks[0],text:'A'.repeat(61)+' · B · C'},blocks[1]]},
    {...record.page,blocks:[blocks[0],{...blocks[1],text:' '}]},
    {...record.page,blocks:[blocks[0],{...blocks[1],text:'x'.repeat(501)}]}
  ]) assert.equal(withPage(invalid),null,JSON.stringify(invalid).slice(0,100));
  assert.deepEqual(withPage(record.page),record.page);
});
test('fixed Academy spotlight is never a public standalone page, sitemap or menu entry',()=>{
  assert.ok(isAcademySpotlightSlug(HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG));
  assert.ok(isHomepageEditorialSlug(HOMEPAGE_ACADEMY_SPOTLIGHT_SLUG));
  const forged={...make(),page:{...make().page,menu_parent:'root'}};
  assert.deepEqual(withWebsitePages([],[forged],academy),[]);
  const pageLoader=read('frontend/src/routes/pages/[slug]/+page.server.ts');
  const sitemap=read('frontend/src/lib/public/seo.ts');
  assert.match(pageLoader,/isHomepageEditorialSlug\(params.slug\)/);
  assert.match(sitemap,/isHomepageEditorialSlug\(record.slug\)/);
});
test('API validates new fixed slot on creation and updates independently of editor',()=>{
  const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
  assert.match(api,/ValidateAcademySpotlight\(owner, body.Slug, body.Page\)/);
  assert.match(api,/ValidateAcademySpotlight\(owner, slug, body.Page\)/);
  assert.match(api,/owner != "lefteria_fc_academy"/);
  assert.match(api,/content.MenuParent != "hidden"/);
  assert.match(api,/content.Blocks.Count != 2/);
  assert.match(api,/content.Blocks\[0\].Text.Split\('·'\).Length != 3/);
  assert.match(api,/requirePublisher && role is not \("super_admin" or "manager"\)/);
  const editor=read('frontend/src/routes/admin/website-pages/+page.svelte');
  assert.match(editor,/openAcademySpotlight/);
  assert.match(editor,/isLockedRegion = \$derived\(isHero \|\| isSpotlight \|\| isAboutIntro \|\| isContactLead \|\| isAboutHistory \|\| isAcademyLead \|\| isShopLead \|\| isGlobalDesign \|\| isNavigationDesign\)/);
  assert.match(editor,/isLockedRegion \? '\/' : websitePagePath/);
  assert.match(editor,/expected_version:selected.version,publish/);
  assert.match(editor,/hasAdminPermission/);
});
test('original homepage dynamic sections and Academy spotlight style remain unchanged',()=>{
  const home=read('frontend/src/routes/+page.svelte');
  const svelte=read('frontend/src/lib/public/HomeAcademySpotlight.svelte');
  const hero=read('frontend/src/lib/public/HomeHero.svelte');
  const editor=read('frontend/src/routes/admin/website-pages/+page.svelte');
  assert.match(home,/homepageAcademySpotlightPage\(websitePages\?\.lefteria_fc_academy/);
  assert.match(home,/editorial=\{academySpotlight\}/);
  assert.match(svelte,/sections.length/);
  assert.match(svelte,/publicImages.hero/);
  assert.match(svelte,/href="\/academy"/);
  assert.match(svelte,/ΑΝΑΠΤΥΞΗ <span>·<\/span> ΠΑΙΔΕΙΑ/);
  assert.match(svelte,/data-testid="home-academy-approved-copy"/);
  assert.match(hero,/fixtureCountdown/);
  assert.doesNotMatch(svelte,/\{@html\}|innerHTML/);
  for(const [p,src] of [
    ['HomeAcademySpotlight.svelte',svelte],
    ['+page.svelte',home],
    ['admin/website-pages/+page.svelte',editor]])
    assert.doesNotThrow(()=>compile(src,{filename:p,generate:'server'}));
});
