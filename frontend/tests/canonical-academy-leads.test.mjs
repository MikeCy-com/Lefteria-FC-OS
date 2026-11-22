import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {canonicalAcademyLead} from '../src/lib/public/canonical-academy.ts';
import {CANONICAL_ACADEMY_INTRO_SLUG, CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG, CANONICAL_ACADEMY_GROUPS_LEAD_SLUG, CANONICAL_ACADEMY_REGISTRATION_LEAD_SLUG,
  isHomepageEditorialSlug, withWebsitePages} from '../src/lib/public/website-pages.ts';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const editor=read('frontend/src/routes/admin/website-pages/+page.svelte');
const landing=read('frontend/src/routes/academy/+page.svelte');
const philosophy=read('frontend/src/routes/academy/philosophy/+page.svelte');
const directory=read('frontend/src/routes/academy/groups/+page.svelte');
const directoryLoader=read('frontend/src/routes/academy/groups/+page.server.ts');
const registration=read('frontend/src/routes/academy/registration/+page.svelte');
const registrationLoader=read('frontend/src/routes/academy/registration/+page.server.ts');
const landingLoader=read('frontend/src/routes/academy/+page.server.ts');
const philosophyLoader=read('frontend/src/routes/academy/philosophy/+page.server.ts');

const row=(slug=CANONICAL_ACADEMY_INTRO_SLUG,organization_id='lefteria_fc_academy')=>({
  organization_id,slug,published_at:'2026-11-22T00:00:00Z',
  page:{title:'Academy approved lead',summary:'',seo_title:'',seo_description:'',
    menu_parent:'hidden',menu_order:0,
    blocks:[{type:'paragraph',text:'Approved Academy copy',url:null}]}
});
test('both Academy lead slots require an exact valid published Academy owner',()=>{
  assert.equal(canonicalAcademyLead([row()],CANONICAL_ACADEMY_INTRO_SLUG),'Approved Academy copy');
  assert.equal(canonicalAcademyLead([row(CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG)],CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG),'Approved Academy copy');
  assert.equal(canonicalAcademyLead([row(CANONICAL_ACADEMY_GROUPS_LEAD_SLUG)],CANONICAL_ACADEMY_GROUPS_LEAD_SLUG),'Approved Academy copy');
  assert.equal(canonicalAcademyLead([row(CANONICAL_ACADEMY_REGISTRATION_LEAD_SLUG)],CANONICAL_ACADEMY_REGISTRATION_LEAD_SLUG),'Approved Academy copy');
  assert.equal(canonicalAcademyLead([row(CANONICAL_ACADEMY_INTRO_SLUG,'lefteria_fc')],CANONICAL_ACADEMY_INTRO_SLUG),null);
  assert.equal(canonicalAcademyLead([{...row(),published_at:null}],CANONICAL_ACADEMY_INTRO_SLUG),null);
  assert.equal(canonicalAcademyLead([{...row(),page:{...row().page,menu_parent:'root'}}],CANONICAL_ACADEMY_INTRO_SLUG),null);
  assert.equal(canonicalAcademyLead([{...row(),page:{...row().page,blocks:[{type:'paragraph',text:'Injected',url:'/admin'}]}}],CANONICAL_ACADEMY_INTRO_SLUG),null);
  assert.equal(canonicalAcademyLead([{...row(),page:{...row().page,blocks:[{type:'image',text:'X',url:'/x'}]}}],CANONICAL_ACADEMY_INTRO_SLUG),null);
  assert.equal(canonicalAcademyLead([{...row(),page:{...row().page,blocks:[{type:'paragraph',text:' ',url:null}]}}],CANONICAL_ACADEMY_INTRO_SLUG),null);
  assert.equal(canonicalAcademyLead([{...row(),page:{...row().page,blocks:[{type:'paragraph',text:'x'.repeat(501),url:null}]}}],CANONICAL_ACADEMY_INTRO_SLUG),null);
});
test('Academy editorial records can never become public standalone CMS pages or menus',()=>{
  for(const slug of [CANONICAL_ACADEMY_INTRO_SLUG,CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG,CANONICAL_ACADEMY_GROUPS_LEAD_SLUG,CANONICAL_ACADEMY_REGISTRATION_LEAD_SLUG]){
    assert.equal(isHomepageEditorialSlug(slug),true);
    assert.deepEqual(withWebsitePages([], [row(slug)], 'lefteria_fc_academy'),[]);
  }
  assert.match(api,/ValidateAcademyEditorialLead\(owner, body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateAcademyEditorialLead\(owner, slug, body\.Page\)/);
  assert.match(api,/owner != "lefteria_fc_academy"/);
  assert.match(api,/content\.MenuParent != "hidden"/);
});
test('native Academy and Philosophy renders retain dynamic groups and native cards',()=>{
  assert.match(landingLoader,/loadSectionPage\(url, 'lefteria_fc_academy'\)/);
  assert.match(landingLoader,/canonicalAcademyLead\(pages, CANONICAL_ACADEMY_INTRO_SLUG\)/);
  assert.match(philosophyLoader,/canonicalAcademyLead\(pages, CANONICAL_ACADEMY_PHILOSOPHY_LEAD_SLUG\)/);
  assert.match(directoryLoader,/canonicalAcademyLead\(pages,CANONICAL_ACADEMY_GROUPS_LEAD_SLUG\)/);
  assert.match(registrationLoader,/canonicalAcademyLead\(pages,CANONICAL_ACADEMY_REGISTRATION_LEAD_SLUG\)/);
  assert.match(registration,/data\.editorialLead \?\?/);
  for(const marker of ['consent_gdpr','consent_medical_auth','data-testid="registration-success"']) assert.ok(registration.includes(marker));
  assert.match(directory,/data\.editorialLead \?\?/);
  for(const marker of ['buildAcademyTerritoryDirectory','groupCoaches','academy-verified-city'])assert.ok(directory.includes(marker));
  assert.match(landing,/data\.editorialLead \?\?/);
  assert.match(philosophy,/data\.editorialLead \?\?/);
  for(const marker of ['academyFixtures','academy-hero-register','academy-hero-groups','buildAcademyTerritoryDirectory'])assert.ok(landing.includes(marker));
  for(const marker of ['philosophy-values','philosophy-approaches'])assert.ok(philosophy.includes(marker));
  assert.ok(compile(landing,{filename:'academy/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(philosophy,{filename:'academy/philosophy/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(directory,{filename:'academy/groups/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(registration,{filename:'academy/registration/+page.svelte',generate:'server'}).js.code.length>100);
});
test('admin uses authorized optimistic drafts and separate manager publication for Academy leads',()=>{
  assert.match(editor,/data-testid="edit-canonical-academy-intro"/);
  assert.match(editor,/data-testid="edit-canonical-academy-philosophy"/);
  assert.match(editor,/data-testid="edit-canonical-academy-groups"/);
  assert.match(editor,/data-testid="edit-canonical-academy-registration"/);
  assert.match(editor,/organization!=='lefteria_fc_academy'/);
  assert.match(editor,/isLockedRegion = \$derived\(isHero \|\| isSpotlight \|\| isAboutIntro \|\| isContactLead \|\| isAboutHistory \|\| isAcademyLead\)/);
  assert.match(editor,/expected_version:selected\?\.version,page:draft/);
  assert.match(editor,/expected_version:selected\.version,publish/);
  assert.ok(compile(editor,{filename:'admin/website-pages/+page.svelte',generate:'server'}).js.code.length>100);
});
