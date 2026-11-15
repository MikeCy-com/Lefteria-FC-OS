import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {homepageHeroPage} from '../src/lib/public/homepage-editorial.ts';
import {HOMEPAGE_HERO_SLUGS,withWebsitePages,isHomeHeroSlug} from '../src/lib/public/website-pages.ts';
const file=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const page=(org)=>({organization_id:org,slug:HOMEPAGE_HERO_SLUGS[org],published_at:'2026-11-15T00:00:00Z',
  page:{title:'Hero',summary:'',seo_title:'',seo_description:'',menu_parent:'hidden',menu_order:0,
    blocks:[{type:'heading',text:'ONE\nTWO',url:null},{type:'paragraph',text:'COPY',url:null}]}});
test('only the correct approved organization hero is read',()=>{
  const academy=page('lefteria_fc_academy'),team=page('lefteria_fc');
  assert.deepEqual(homepageHeroPage([team,academy],'lefteria_fc_academy'),academy.page);
  assert.deepEqual(homepageHeroPage([academy,team],'lefteria_fc'),team.page);
  assert.equal(homepageHeroPage([team],'lefteria_fc_academy'),null);
  assert.equal(homepageHeroPage([{...academy,page:{...academy.page,menu_parent:'root'}}],'lefteria_fc_academy'),null);
  assert.equal(homepageHeroPage([{...team,page:{...team.page,blocks:[...team.page.blocks,{type:'image',text:'',url:'/asset'}]}}],'lefteria_fc'),null);
  assert.equal(homepageHeroPage([],'lefteria_fc'),null);
});
test('hero snapshots never enter public menus or public standalone page routes',()=>{
  const team=page('lefteria_fc');
  assert.ok(isHomeHeroSlug(team.slug));
  assert.deepEqual(withWebsitePages([],[{...team,page:{...team.page,menu_parent:'root'}}],'lefteria_fc'),[]);
  assert.match(file('frontend/src/routes/pages/[slug]/+page.server.ts'),/isHomepageEditorialSlug\(params.slug\)/);
});
test('backend allows only two fixed, hidden, owner-scoped hero text blocks',()=>{
  const api=file('backend/src/LefteriaFc.Api/PublicContent/WebsitePageEndpoints.cs');
  assert.match(api,/ValidateHomeHero\(owner, body.Slug, body.Page\)/);
  assert.match(api,/ValidateHomeHero\(owner, slug, body.Page\)/);
  assert.match(api,/content.MenuParent != "hidden"/);
  assert.match(api,/content.Blocks.Count != 2/);
  assert.match(api,/owner != requiredOwner/);
  assert.match(api,/role is not \("super_admin" or "manager"\)/);
});
test('original hero dynamic carousel and match components are preserved',()=>{
  const hero=file('frontend/src/lib/public/HomeHero.svelte');
  const admin=file('frontend/src/routes/admin/website-pages/+page.svelte');
  assert.match(hero,/nextPublicFixture/);
  assert.match(hero,/fixtureCountdown/);
  assert.match(hero,/hero-slide-toggle/);
  assert.match(hero,/firstTeamHeroBanner/);
  assert.match(hero,/academyDescription/);
  assert.match(hero,/teamDescription/);
  assert.doesNotMatch(hero,/\{@html\}|innerHTML/);
  assert.match(admin,/openHomepageHero/);
  assert.match(admin,/isLockedRegion \|\| index===0/);
});
