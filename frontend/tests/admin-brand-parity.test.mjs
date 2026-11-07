import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=(relative)=>readFileSync(new URL('../src/lib/admin/'+relative,import.meta.url),'utf8');

test('admin retains original Fira Sans/condensed and Lefteria gold rather than a generic blue redesign',()=>{
  const css=read('admin.css');
  assert.match(css,/Fira Sans Extra Condensed/);
  assert.match(css,/Fira Sans/);
  assert.match(css,/--admin-gold:#f5a623/);
  assert.match(css,/--admin-black:#080808/);
  assert.match(css,/@media \(min-width:2560px\)/);
  assert.match(css,/1800px/);
  assert.match(css,/prefers-reduced-motion/);
  assert.match(css,/prefers-contrast/);
});

test('admin visual refinements do not alter real role permissions or navigation',()=>{
  const frame=read('AdminFrame.svelte');
  assert.match(frame,/canReadClub\(user\)/);
  assert.match(frame,/data-testid="admin-logout"/);
  assert.match(frame,/href="\/admin\/club"/);
  assert.match(frame,/src="\/mobile\/club-logo.png"/);
  assert.match(frame,/id="admin-main"/);
});
