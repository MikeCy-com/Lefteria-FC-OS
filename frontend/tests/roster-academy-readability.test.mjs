import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const read = path => readFileSync(new URL('../src/' + path, import.meta.url), 'utf8');

test('team roster exposes filter results and allows restoring the full roster', () => {
  const source = read('routes/team/+page.svelte');
  assert.match(source, /role="search" aria-label="Αναζήτηση παικτών"/);
  assert.match(source, /aria-controls="roster-results"/);
  assert.match(source, /id="roster-results"/);
  assert.match(source, /role="status" aria-live="polite"/);
  assert.match(source, /filtered\.length/);
  assert.match(source, /search = ''; position = '';/);
  assert.match(source, /roster-reset/);
  assert.match(source, /aria-current=\{data\.tab === id \? 'page'/);
  assert.doesNotThrow(() => compile(source, {filename:'team/+page.svelte',generate:'server'}));
});

test('academy navigation scrolls to labelled sections, cards remain keyboard accessible', () => {
  const source = read('routes/academy/+page.svelte');
  for (const id of ['explore', 'groups', 'academy-fixtures']) {
    assert.ok(source.includes('id="' + id + '"'), 'Missing academy section ' + id);
  }
  assert.match(source, /aria-labelledby="academy-groups-title"/);
  assert.match(source, /aria-labelledby="academy-fixtures-title"/);
  const css = read('lib/public/public.css');
  assert.match(css, /scroll-margin-top:calc\(var\(--public-header,76px\) \+ 24px\)/);
  assert.match(css, /\.academy-destination:focus-visible/);
  assert.doesNotThrow(() => compile(source, {filename:'academy/+page.svelte',generate:'server'}));
});
