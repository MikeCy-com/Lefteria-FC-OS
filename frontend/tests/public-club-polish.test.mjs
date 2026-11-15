import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source = (path) => readFileSync(new URL('../src/lib/public/'+path,import.meta.url),'utf8');
const shell = source('PublicShell.svelte');
const nav = source('PublicNavigation.svelte');
const css = source('club-polish.css');

test('public design finish is shared by all PublicShell pages and does not style admin/app',()=>{
  assert.match(shell,/import '\.\/club-polish\.css';/);
  assert.match(shell,/class="public-site"/);
  assert.match(css,/\.public-site \.public-page-heading/);
  assert.match(css,/\.public-site \.home-reference/);
  assert.match(css,/\.public-site \.academy-intro/);
  assert.match(css,/\.public-site \.fixture-row/);
  assert.doesNotMatch(css,/(^|\n)\s*(\.admin-|\.mobile-|body\s*\{|:root\s*\{)/);
});

test('navigation labels correspond to the actual destination',()=>{
  assert.match(nav,/href="\/shop" class="public-button public-ticket-link"[^>]*>ΚΑΤΑΣΤΗΜΑ/);
  assert.match(nav,/href="\/app\/login"/);
  assert.match(nav,/data-testid="public-menu-drawer"/);
});

test('mobile accessibility and reduced motion remain first-class',()=>{
  assert.match(css,/@media\(max-width:767px\)/);
  assert.match(css,/overflow-x:auto/);
  assert.match(css,/overscroll-behavior-inline:contain/);
  assert.match(css,/prefers-reduced-motion:reduce/);
  assert.match(css,/focus-visible/);
  assert.match(css,/--public-gold/);
});

test('homepage organization boundaries are visible in the media and fixture selectors',()=>{
  const hero = source('HomeHero.svelte');
  const cards = source('HomeDestinations.svelte');
  const spotlight = source('HomeAcademySpotlight.svelte');
  assert.match(hero,/site\?\.fixtures\.filter\(row => row\.organization_id === 'lefteria_fc'\)/);
  assert.match(cards,/firstTeamHeroBanner\(site\?\.teams\)/);
  assert.match(cards,/group\.organization_id === 'lefteria_fc_academy'/);
  assert.match(cards,/Πρόγραμμα & αποτελέσματα/);
  assert.match(spotlight,/group\.organization_id === 'lefteria_fc_academy'/);
});

test('catalog, legal and journey templates receive the same restrained club finish',()=>{
  for(const selector of ['.catalog-heading','.catalog-card','.catalog-detail-image',
    '.source-legal-document','.source-journey','.source-about','.public-news-list'])
    assert.ok(css.includes('.public-site '+selector),'missing shared polish '+selector);
  assert.match(css,/\[data-testid="push-toggle"\] \{display:none;\}/);
});
