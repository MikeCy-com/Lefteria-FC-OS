import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';
const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const home=read('src/routes/+page.svelte');
const css=read('src/lib/public/home-reference.css');
const base=read('src/lib/public/public.css');
const audit=read('scripts/ui-visual-audit.mjs');
const nav=read('src/lib/public/PublicNavigation.svelte');

test('homepage follows 1672×941 screenshot region order without hiding CMS and app functions',()=>{
  assert.doesNotThrow(()=>compile(home,{filename:'routes/+page.svelte',generate:'server'}));
  const required=[
    '<HomeHero','<HomeDestinations',
    'data-testid="home-brand-interval"','<HomeNews','<Fixtures','<HomeAcademySpotlight','<SidelineBanner'
  ];
  let cursor=-1;
  for(const marker of required){const index=home.indexOf(marker);assert.ok(index>cursor,'Missing/out-of-order '+marker);cursor=index;}
  assert.match(home,/data-testid="home-source-unavailable"/);
  assert.match(home,/\{#if !data\.available && data\.newsAvailable\}/);
  assert.match(home,/site=\{data\.site\} groups=\{data\.navigationGroups\}/);
  assert.match(home,/available=\{data\.newsAvailable\}/);
  assert.match(home,/ONE TEAM/); assert.match(home,/ONE DREAM/);
  assert.doesNotMatch(home,/Math\.random|dummy|fake fixture/);
});

test('desktop geometry keeps the news visible without an empty brand gap and mobile remains accessible',()=>{
  assert.match(base,/@media\(min-width:1200px\)/);
  assert.match(base,/--public-rail:154px/);
  assert.match(base,/--public-header:64px/);
  assert.match(base,/\.public-rail nav :is\(a,button\)/);
  assert.match(css,/height:clamp\(408px,24\.5vw,860px\)/);
  assert.match(css,/height:105px;aspect-ratio:auto/);
  assert.match(css,/min-height:clamp\(260px,21vw,370px\)/);
  assert.match(css,/\.home-brand-interval/);
  assert.match(css,/\.home-brand-motto/);
  assert.match(css,/@media\(max-width:767px\)/);
  for(const text of ['homeTiles:rect','homeFixture:rect','homeMotto:rect','homeNews:rect','hero.height<400','news.y>900'])
    assert.ok(audit.includes(text),'No browser measurement: '+text);
  assert.match(nav,/data-testid="public-sidebar"/);
  assert.match(nav,/data-testid="public-menu-drawer"/);
  assert.match(nav,/aria-haspopup="dialog"/);
});
