import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const original = JSON.parse(readFileSync(new URL('../../docs/parity/original-admin-functionality-map.json', import.meta.url),'utf8'));
const css = readFileSync(new URL('../src/lib/public/home-reference.css',import.meta.url),'utf8');
const base = readFileSync(new URL('../src/lib/public/public.css',import.meta.url),'utf8');
const admin = readFileSync(new URL('../src/lib/admin/admin.css',import.meta.url),'utf8');
const mobile = readFileSync(new URL('../src/lib/mobile/components/mobile-theme.css',import.meta.url),'utf8');
const audit = readFileSync(new URL('../scripts/ui-visual-audit.mjs',import.meta.url),'utf8');

test('source admin action inventory accounts for all 58 nonheading original actions', () => {
  assert.equal(original.source_repository,'MikeCy-com/Lefteria-FC');
  assert.equal(original.features.length,58);
  assert.equal(new Set(original.features.map(x=>x.source_id)).size,58);
  assert.ok(original.features.every(x=>x.owner&&x.phase&&x.acceptance_gap&&x.status));
  assert.ok(original.features.filter(x=>x.phase==='mvp').length>=25);
  assert.ok(original.features.some(x=>x.source_id==='livescore'&&!x.href));
  assert.ok(original.features.some(x=>x.source_id==='polls'&&!x.href));
  assert.ok(original.features.some(x=>x.source_id==='drills'&&!x.href));
  assert.ok(original.features.some(x=>x.source_id==='settings_stripe'&&x.phase==='later'));
  const academy = original.features.find(x=>x.source_id==='academy_opponents');
  assert.equal(academy.owner,'academy');
  assert.match(academy.acceptance_gap,/city-level/);
  assert.ok(original.deployment_rule.includes('NEVER touch legacy production'));
});

test('homepage restores Fira type and compact right-hand rectangular original fixture', () => {
  assert.match(base,/body:has\(\.public-site\) \{ font-family:'Fira Sans'/);
  assert.match(base,/\.public-site, \.public-dialog \{[^\n]*font-family:'Fira Sans'/);
  assert.match(css,/grid-template-columns:minmax\(0,1fr\) minmax\(0,320px\)/);
  assert.match(css,/grid-column:2/);
  assert.match(css,/max-width:320px/);
  assert.match(css,/justify-self:end/);
  assert.match(css,/width:clamp\(285px,18vw,320px\)/);
  assert.match(css,/clip-path:none/);
  assert.match(css,/font:italic 900 clamp\(48px,3\.5vw,58px\)/);
  assert.match(audit,/fixture\.width>335/);
  assert.match(audit,/fixture\.x < copy\.right\+12/);
  assert.match(audit,/publicFont\.family\.replace\(/);
});

test('admin navigation remains accessible, with original body font and clearer categories', () => {
  assert.match(admin,/font-family:'Manrope',-apple-system,BlinkMacSystemFont,sans-serif/);
  assert.match(admin,/font:800 17px\/1\.3 'Fira Sans Extra Condensed'/);
  assert.match(admin,/font:600 14px\/1\.4 'Manrope'/);
  assert.match(admin,/min-height:44px/);
  assert.match(admin,/\.admin-nav-category:focus-visible/);
});

test('mobile app original role typography remains untouched by this parity pass', () => {
  for(const value of ['--lf-type-display: 26px','--lf-type-title: 16px','--lf-type-card: 14px',
    '--lf-type-body: 13px','--lf-type-label: 12px','--lf-type-caption: 10px'])
    assert.ok(mobile.includes(value),value);
});
