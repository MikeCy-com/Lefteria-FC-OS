import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

test('match detail uses original Fira heading family and readable mobile labels', () => {
  const path = new URL('../src/routes/match/[id]/+page.svelte', import.meta.url);
  const source = readFileSync(path, 'utf8');
  assert.ok(source.includes("'Fira Sans Extra Condensed',sans-serif"));
  assert.ok(!source.includes("'Fira Sans Condensed',sans-serif"));
  assert.match(source, /\.match-team>span\{font-size:\.75rem;line-height:1\.4/);
  assert.match(source, /\.match-score small\{display:block;color:#d1d1d1;font-size:\.8rem/);
  assert.match(source, /grid-template-columns:minmax\(0,1fr\) minmax\(70px,\.55fr\) minmax\(0,1fr\)/);
  assert.match(source, /aria-label=\{hasScore/);
  assert.doesNotThrow(() => compile(source, {filename:'match/[id]/+page.svelte',generate:'server'}));
});
