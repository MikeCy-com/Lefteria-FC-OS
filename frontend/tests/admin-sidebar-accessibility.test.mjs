import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const read = path=>readFileSync(new URL('../src/lib/admin/'+path,import.meta.url),'utf8');
const css=read('admin.css');
const frame=read('AdminFrame.svelte');
const marker='/* MVP navigation: one level of permission-scoped disclosures.';
const compact=css.split(marker)[1];

test('admin navigation uses larger work-area labels than links, not inverted typography',()=>{
  assert.ok(compact,'MVP navigation overrides must follow the legacy CSS layers');
  assert.match(css,/font:800 17px\/1\.3 'Fira Sans Extra Condensed'/);
  assert.match(css,/font:600 14px\/1\.4 'Manrope'/);
  assert.match(compact,/font-size:19px/);
  assert.match(compact,/font-size:16px/);
  assert.match(compact,/font-size:14px/);
  assert.match(compact,/white-space:normal/);
  assert.match(compact,/overflow-wrap:anywhere/);
  assert.match(compact,/min-height:44px/);
  assert.match(compact,/min-height:48px/);
});

test('native one-level category disclosures expose keyboard focus and selected-page contrast',()=>{
  assert.ok(compile(frame,{filename:'AdminFrame.svelte',generate:'server'}).js.code.length>100);
  assert.equal((frame.match(/class="admin-nav-disclosure"/g)||[]).length,5);
  assert.match(frame,/<details class="admin-nav-disclosure"/);
  assert.match(frame,/<summary class="admin-nav-category"/);
  assert.match(compact,/\.admin-nav-disclosure:not\(\[open\]\) > \.admin-nav-links/);
  assert.match(compact,/\.admin-nav-category:focus-visible/);
  assert.match(compact,/outline:3px solid #ffca65/);
  assert.match(compact,/\.admin-nav-links a\[aria-current='page'\]/);
  assert.match(compact,/@media\(prefers-contrast:more\)/);
  assert.match(compact,/@media\(prefers-reduced-motion:reduce\)/);
  assert.match(frame,/aria-label="Διαχείριση συλλόγου"/);
  assert.match(frame,/href="#admin-main"/);
  assert.match(frame,/aria-current=\{active==='account'/);
});

test('phone admin menu is a compact scannable index, not a horizontal oversized scroller',()=>{
  assert.match(compact,/@media\(max-width:720px\)/);
  assert.match(compact,/grid-template-columns:repeat\(2,minmax\(0,1fr\)\)/);
  assert.match(compact,/max-height:min\(58dvh,500px\)/);
  assert.match(compact,/overflow-x:hidden/);
  assert.match(compact,/overflow-y:auto/);
  assert.match(compact,/grid-column:1\/-1/);
  assert.match(compact,/@media\(max-width:360px\)/);
  assert.match(compact,/grid-template-columns:1fr/);
  assert.match(compact,/scrollbar-width:thin/);
});

test('visibility remains driven only by live modules, verified roles and organizations',()=>{
  for(const check of [
    'adminSectionsFor(user)',"module.status==='live'","liveIds.has('groups')",
    "liveIds.has('pages')","liveIds.has('news')","liveIds.has('announcements')",
    "liveIds.has('products')","liveIds.has('sponsors')",'canViewFirstTeamTrials(user)'
  ]) assert.ok(frame.includes(check),check);
  for(const id of ['admin-news-link','admin-announcements-link','admin-payabl-link',
    'admin-groups-link','admin-territories-link','admin-hub-subscriptions-link',
    'admin-settings-link'])assert.ok(frame.includes(id),id);
  assert.doesNotMatch(frame,/href="\/admin\/(?:evaluations|documents|matches)"/);
  const assigned=frame.match(/\{#if liveIds\.has\('assigned_groups'\)\}([\s\S]*?)\{\/if\}/)?.[1]??'';
  assert.match(assigned,/href="\/admin\/schedule"/);
  assert.match(assigned,/href="\/admin\/attendance"/);
});
