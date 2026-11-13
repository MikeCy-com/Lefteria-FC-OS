import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const read = path => readFileSync(new URL('../src/' + path, import.meta.url), 'utf8');

test('public and mobile accessibility rules load from shared app shell', () => {
  const layout = read('routes/+layout.svelte');
  const css = read('lib/accessibility.css');
  assert.match(layout, /import '\.\.\/lib\/accessibility\.css'/);
  assert.match(css, /prefers-reduced-motion: reduce/);
  assert.match(css, /prefers-contrast: more/);
  assert.match(css, /forced-colors: active/);
  assert.match(css, /focus-visible/);
  assert.match(css, /overflow-x: auto/);
  assert.match(css, /scroll-margin-top/);
  assert.match(css, /\.public-site/);
  assert.match(css, /\.lf-mobile-theme/);
  assert.doesNotMatch(css, /(?:^|\n)\s*(?:body|html|button|a)\s*\{/m,
    'Accessibility rules should not globally redesign admin or unrelated routes');
});

test('the OS retains original brand typography while adding accessibility overrides', () => {
  const mobile = read('lib/mobile/components/mobile-theme.css');
  const publicCss = read('lib/public/public.css');
  assert.match(mobile, /Fira Sans Extra Condensed/);
  assert.match(publicCss, /Fira Sans Extra Condensed/);
  assert.match(mobile, /--lf-type-body: 13px/);
  assert.match(publicCss, /--public-gold:#f5a623/);
});
