import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');

test('Original club-site source font stack is self-hosted and scoped to public pages only',()=>{
  const css=read('frontend/src/lib/public/public.css');
  const fonts=read('frontend/src/lib/public/fonts.ts');
  const admin=read('frontend/src/lib/admin/admin.css');
  const baseline=read('docs/parity/visual-baseline.md');
  assert.match(fonts,/@fontsource\/manrope\/400\.css/);
  assert.match(fonts,/@fontsource\/manrope\/700\.css/);
  assert.match(css,/\.public-site, \.public-dialog \{[^\n]*font-family:'Fira Sans'/);
  assert.match(css,/\.public-site input:not\(\[type="checkbox"\]\)/);
  assert.match(css,/\.public-site \.catalog-heading h1,/);
  assert.match(css,/\[data-testid="academy-groups-hero"\] h1/);
  assert.match(css,/font-size:58px/);
  assert.match(css,/@media\(max-width:650px\)/);
  assert.match(css,/font-size:38px/);
  assert.match(admin,/\.admin-app/);
  assert.doesNotMatch(css,/^body\s*\{[^}]*font-family:Manrope/m);
  assert.match(baseline,/live Manrope 16px vs OS Inter 16px/);
  assert.match(baseline,/academy\/groups.*58px/);
});

test('Chromium captures include Academy group view and check true computed typography',()=>{
  const script=read('frontend/scripts/ui-visual-audit.mjs');
  const groups=read('frontend/src/routes/academy/groups/+page.svelte');
  assert.match(groups,/data-testid="academy-groups-hero"/);
  assert.match(script,/name:'academy-groups-unavailable', path:'\/academy\/groups', expectedStatus:503/);
  assert.match(script,/getComputedStyle\(root\)\.fontFamily/);
  assert.match(script,/publicFont\.family\.replace\(/);
  assert.match(script,/viewport\.width <= 650 \? 38 : 58/);
  assert.match(script,/results\.push\([^\n]*publicFont/);
  assert.doesNotMatch(script,/https:\/\/lefteriafc\.cy.*page\.goto/);
});
