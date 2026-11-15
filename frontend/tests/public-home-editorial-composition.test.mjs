import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {render} from 'svelte/server';

const read=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const css=read('src/lib/public/home-reference.css');
const page=read('src/routes/+page.svelte');
const spotlight=read('src/lib/public/HomeAcademySpotlight.svelte');
const news=read('src/lib/public/NewsCards.svelte');
const homepage=read('src/lib/public/HomeNews.svelte');

test('reference homepage preserves its working news CMS, filters, navigation and Academy CTA',()=>{
  for(const [name,source] of [
    ['routes/+page.svelte',page],
    ['HomeAcademySpotlight.svelte',spotlight],
    ['HomeNews.svelte',homepage],
    ['NewsCards.svelte',news]
  ])assert.ok(compile(source,{filename:name,generate:'server'}).js.code.length>100);
  assert.match(page,/<HomeAcademySpotlight site=\{data\.site\} groups=\{data\.navigationGroups\}/);
  assert.match(page,/<SidelineBanner \/>/);
  assert.match(page,/<HomeNews articles=\{data\.news\?\.items \?\? \[\]\} available=\{data\.newsAvailable\}/);
  assert.match(homepage,/article\.category===filter|article\.category === filter/);
  assert.match(homepage,/NewsCards articles=\{rows\}/);
  assert.match(news,/articleLink\(article,newsScope\)/);
  assert.match(spotlight,/data-testid="home-academy-spotlight"/);
  assert.match(spotlight,/href="\/academy"/);
  assert.match(spotlight,/sections\.length/);
  assert.doesNotMatch(spotlight,/href="\/team"|hardcoded.*sponsor|localStorage/);
});
test('news composition matches one lead card and a three-card right rail at desktop widths',()=>{
  assert.match(css,/@media\(min-width:1024px\)/);
  assert.match(css,/grid-template-columns:minmax\(0,1\.12fr\) minmax\(0,1fr\)/);
  assert.match(css,/grid-template-rows:repeat\(3,minmax\(160px,auto\)\)/);
  assert.match(css,/\.public-news-card:first-child\s*\{\s*grid-column:1;grid-row:1 \/ span 3/);
  assert.match(css,/\.public-news-card:not\(:first-child\)\s*\{\s*grid-column:2;grid-row:auto/);
  assert.match(css,/\.public-news-card:only-child\s*\{\s*grid-column:1 \/ -1/);
  assert.match(css,/@media\(min-width:768px\) and \(max-width:1023px\)/);
  assert.match(css,/@media\(max-width:767px\)/);
  assert.match(css,/grid-template-columns:minmax\(0,1fr\);grid-auto-rows:auto/);
});
test('Academy spotlight renders navigation without any unverified sponsor content',async()=>{
  const {default:Component}=await import('../src/lib/public/HomeAcademySpotlight.svelte');
  const output=render(Component,{props:{site:null,groups:[]}}).body;
  assert.match(output,/data-testid="home-academy-spotlight"/);
  assert.match(output,/data-testid="home-academy-link"/);
  assert.match(output,/href="\/academy"/);
  assert.doesNotMatch(output,/sponsor-logo|undefined|href="\/team"/);
});
