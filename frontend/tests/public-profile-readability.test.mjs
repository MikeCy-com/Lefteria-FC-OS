import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const read = path => readFileSync(new URL('../src/' + path, import.meta.url), 'utf8');

const css = read('lib/public/public.css');

test('public person profile keeps the roster link and semantic facts', () => {
  const source = read('lib/public/PublicPersonProfile.svelte');
  assert.match(source, /\/team\?tab=roster/);
  assert.doesNotMatch(source, /person-breadcrumb/);
  assert.match(source, /<dl class="person-facts">/);
  assert.match(source, /<dt>\{label\}<\/dt><dd>\{value\}<\/dd>/);
  assert.match(css, /font-size:13px;line-height:1\.5/);
  assert.doesNotThrow(() => compile(source, { filename: 'PublicPersonProfile.svelte', generate: 'server' }));
});

test('player statistics use readable responsive cards without inventing unknown values', () => {
  const source = read('lib/public/PlayerStatistics.svelte');
  assert.match(source, /value \?\? '—'/);
  assert.match(css, /font-size:13px;line-height:1\.45/);
  assert.match(css, /grid-template-columns:repeat\(2,minmax\(0,1fr\)\)/);
  assert.match(css, /@media\(max-width:480px\)/);
  assert.doesNotThrow(() => compile(source, { filename: 'PlayerStatistics.svelte', generate: 'server' }));
});
