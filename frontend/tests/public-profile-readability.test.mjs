import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const read = path => readFileSync(new URL('../src/' + path, import.meta.url), 'utf8');

test('public person profile preserves navigation context and semantic facts', () => {
  const source = read('lib/public/PublicPersonProfile.svelte');
  assert.match(source, /aria-label="Διαδρομή σελίδας"/);
  assert.match(source, /aria-current="page"/);
  assert.match(source, /<dl class="person-facts">/);
  assert.match(source, /<dt>\{label\}<\/dt><dd>\{value\}<\/dd>/);
  assert.match(source, /\.person-breadcrumb \[aria-current="page"\]/);
  assert.match(source, /font-size:13px;line-height:1\.5/);
  assert.doesNotThrow(() => compile(source, { filename: 'PublicPersonProfile.svelte', generate: 'server' }));
});

test('player statistics use readable responsive cards without inventing unknown values', () => {
  const source = read('lib/public/PlayerStatistics.svelte');
  assert.match(source, /value \?\? '—'/);
  assert.match(source, /font-size:13px;line-height:1\.45/);
  assert.match(source, /grid-template-columns:repeat\(2,minmax\(0,1fr\)\)/);
  assert.match(source, /@media\(max-width:480px\)/);
  assert.doesNotThrow(() => compile(source, { filename: 'PlayerStatistics.svelte', generate: 'server' }));
});
