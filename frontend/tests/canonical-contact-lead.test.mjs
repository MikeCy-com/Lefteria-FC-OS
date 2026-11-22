import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {canonicalContactLead} from '../src/lib/public/canonical-contact.ts';
import {CANONICAL_CONTACT_LEAD_SLUG,isHomepageEditorialSlug,withWebsitePages} from '../src/lib/public/website-pages.ts';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
const admin=read('frontend/src/routes/admin/website-pages/+page.svelte');
const contact=read('frontend/src/routes/contact/+page.svelte');
const loader=read('frontend/src/routes/contact/+page.server.ts');
const row=(owner='lefteria_fc')=>({organization_id:owner,slug:CANONICAL_CONTACT_LEAD_SLUG,
  published_at:'2026-11-22T00:00:00Z',page:{title:'Contact introduction',summary:'',seo_title:'',
  seo_description:'',menu_parent:'hidden',menu_order:0,
  blocks:[{type:'paragraph',text:'A verified contact introduction',url:null}]}});

test('Contact lead renders only an approved owner-specific published paragraph',()=>{
  assert.equal(canonicalContactLead([row()],'lefteria_fc'),'A verified contact introduction');
  assert.equal(canonicalContactLead([row()],'lefteria_fc_academy'),null);
  assert.equal(canonicalContactLead([row('lefteria_fc_academy')],'lefteria_fc_academy'),'A verified contact introduction');
  assert.equal(canonicalContactLead([{...row(),published_at:null}],'lefteria_fc'),null);
  assert.equal(canonicalContactLead([{...row(),page:{...row().page,menu_parent:'root'}}],'lefteria_fc'),null);
  assert.equal(canonicalContactLead([{...row(),page:{...row().page,blocks:[{type:'button',text:'Bad',url:'/bad'}]}}],'lefteria_fc'),null);
  assert.equal(canonicalContactLead([{...row(),page:{...row().page,blocks:[{type:'paragraph',text:'Bad',url:'/bad'}]}}],'lefteria_fc'),null);
  assert.equal(canonicalContactLead([{...row(),page:{...row().page,blocks:[{type:'paragraph',text:'',url:null}]}}],'lefteria_fc'),null);
});

test('canonical Contact slot stays out of menus, routes and public contact data',()=>{
  assert.equal(isHomepageEditorialSlug(CANONICAL_CONTACT_LEAD_SLUG),true);
  assert.equal(withWebsitePages([], [row()], 'lefteria_fc').length,0);
  assert.match(api,/ValidateCanonicalContactLead\(body\.Slug, body\.Page\)/);
  assert.match(api,/ValidateCanonicalContactLead\(slug, body\.Page\)/);
  assert.match(loader,/canonicalContactLead\(pages,site\.organization\)/);
  assert.match(contact,/data\.contactLead \?\?/);
  assert.match(contact,/contactEmailDraftHref/);
  assert.match(contact,/safeContactEmail\(club\?\.email\)/);
  assert.match(contact,/data-testid="contact-form"/);
  assert.ok(compile(contact,{filename:'contact/+page.svelte',generate:'server'}).js.code.length>100);
});

test('admin edits the Contact intro via actual scoped draft and separately approved publication',()=>{
  assert.match(admin,/data-testid="edit-canonical-contact-lead"/);
  assert.match(admin,/openCanonicalContactLead/);
  assert.match(admin,/isHero \|\| isSpotlight \|\| isAboutIntro \|\| isContactLead \|\| isAboutHistory \|\| isAcademyLead/);
  assert.match(admin,/expected_version:selected\?\.version,page:draft/);
  assert.match(admin,/expected_version:selected\.version,publish/);
  assert.match(admin,/\/contact\?organization_id=lefteria_fc_academy/);
});
