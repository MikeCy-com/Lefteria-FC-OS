import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../src/lib/public/'+p,import.meta.url),'utf8');
const hero=read('HomeHero.svelte');
const reference=read('home-reference.css');
const styles=read('public.css');
const navigation=read('PublicNavigation.svelte');
const footer=read('PublicFooter.svelte');

test('reference hero uses canonical editorial asset independent of academy groups',()=>{
  assert.match(hero,/const backgrounds = \$derived\(\[publicImages\.hero, firstTeam\?\.banner_url/);
  assert.match(hero,/data-testid="hero-carousel"/);
  assert.match(hero,/data-testid="public-hero-fixture"/);
  assert.match(hero,/data-testid="hero-slide-controls"/);
  assert.match(reference,/\.public-site \.home-reference \.public-hero-stage/);
  assert.match(reference,/\.public-site \.home-reference \.public-hero-fixture::before/);
  assert.match(reference,/\.public-site \.home-reference \.public-destination\s*\{\s*clip-path:polygon/);
  assert.match(reference,/\.public-site \.home-reference \.public-news-card:hover/);
});
test('desktop rail suppresses duplicate hamburger, collapsed header restores it',()=>{
  assert.match(styles,/@media \(min-width:768px\)\s*\{\s*\.public-site \.public-header > \[data-testid="public-menu-open"\]\s*\{\s*display:none/);
  assert.match(styles,/@media \(max-width:767px\)\s*\{\s*\.public-site \.public-header > \[data-testid="public-menu-open"\]\s*\{\s*display:inline-flex/);
  assert.match(navigation,/data-testid="public-sidebar"/);
  assert.match(navigation,/data-testid="public-menu-drawer"/);
});
test('public profile entry uses actual standalone OTP login in separate tab',()=>{
  assert.match(navigation,/href="\/app\/login" target="_blank" rel="noopener noreferrer"[^>]+data-testid="public-account-link"/);
  assert.match(navigation,/href="\/app\/login" target="_blank" rel="noopener noreferrer"[^>]+data-testid="public-menu-account"/);
  assert.match(footer,/\['profile','Το προφίλ μου','\/app\/login','user'\]/);
  assert.match(footer,/target=\{id === 'profile' \? '_blank'/);
  for(const [name,source] of [['HomeHero',hero],['PublicNavigation',navigation],['PublicFooter',footer]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
});
