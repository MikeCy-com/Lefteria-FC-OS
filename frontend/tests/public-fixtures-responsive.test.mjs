import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const source = readFileSync(new URL('../src/lib/public/Fixtures.svelte', import.meta.url), 'utf8');

const css = readFileSync(new URL('../src/lib/public/public.css', import.meta.url), 'utf8');

test('public fixtures compile and preserve match navigation for mobile and desktop', () => {
  assert.doesNotThrow(() => compile(source, { filename:'Fixtures.svelte', generate:'server' }));
  assert.match(source, /fixture-detail-link/);
  assert.match(source, /fixture-mobile-detail-link/);
  assert.match(source, /encodeURIComponent\(fixture\.id\)/);
  assert.match(css, /min-height:44px/);
});

test('narrow fixture cards handle long team names and readable date / score', () => {
  assert.match(css, /overflow-wrap:anywhere/);
  assert.match(css, /grid-template-columns:minmax\(0,1fr\) auto minmax\(0,1fr\)/);
  assert.match(css, /@media\(max-width:767px\)/);
  assert.match(css, /@media\(max-width:360px\)/);
  assert.match(css, /fixture-row time \{display:block;color:#ddd;font-size:14px/);
  assert.match(css, /fixture-teams span \{font-size:clamp\(21px,6vw,28px\)/);
});
