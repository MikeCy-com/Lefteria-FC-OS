import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../'+path,import.meta.url),'utf8');
const skin=read('src/lib/public/club-polish.css');
const base=read('src/lib/public/public.css');
const person=read('src/lib/public/PublicPersonProfile.svelte');
const shell=read('src/lib/public/PublicShell.svelte');
const nav=read('src/lib/public/navigation.ts');

test('public section headings have exactly one leading gold accent, not doubled or beneath',()=>{
  const section=skin.match(/\.public-site :is\(\.public-section-heading,\.section-heading\) h2::before\s*\{([^}]+)\}/)?.[1];
  assert.ok(section,'canonical heading accent rule missing');
  assert.match(section,/content:""/);
  assert.match(section,/flex:0 0 5px/);
  assert.match(section,/background:var\(--public-gold\)/);
  assert.match(section,/box-shadow:none/);
  assert.doesNotMatch(section,/box-shadow:5px/);
  assert.match(base,/\.public-site \.section-heading h2::before/,'legacy accent remains but is overridden by final skin');
});

test('player profile preserves existing published data and restores three-part original hero',()=>{
  assert.match(person,/data-testid=\{player \? "player-hero" : undefined\}/);
  assert.match(person,/data-testid=\{player \? 'player-hero-photo' : undefined\}/);
  assert.match(person,/data-testid="player-stat-bars"/);
  assert.match(person,/grid-template-columns:minmax\(190px,280px\) minmax\(0,1fr\) minmax\(190px,280px\)/);
  assert.match(person,/player\.statistics\?\.appearances/);
  assert.match(person,/player\.statistics\?\.goals/);
  assert.match(person,/player\.statistics\?\.assists/);
  assert.match(person,/player\.statistics\?\.minutes_played/);
  assert.match(person,/isAcademyPlayer/);
  assert.match(person,/PlayerStatistics statistics=\{player.statistics\}/);
  assert.match(person,/player-gallery/);
  assert.ok(compile(person,{filename:'PublicPersonProfile.svelte',generate:'server'}).js.code.length>100);
});

test('CMS sections render inside native page without cross-organization borrowing',()=>{
  assert.match(shell,/embeddedWebsiteSections\(page.data\?\.websitePages\?\.\[organization\]/);
  assert.match(shell,/data-testid="public-embedded-page"/);
  assert.match(shell,/WebsitePageBlocks blocks=\{section.page.blocks\}/);
  assert.match(nav,/const expected='page-'\+owner\+'-'\+slug/);
  assert.ok(compile(shell,{filename:'PublicShell.svelte',generate:'server'}).js.code.length>100);
});
