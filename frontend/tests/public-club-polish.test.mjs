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
