import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const frame=read('lib/admin/AdminFrame.svelte');
const css=read('lib/admin/admin.css');

test('football section title distinguishes Academy, First Team, and dual assignment',()=>{
  assert.ok(compile(frame,{filename:'AdminFrame.svelte',generate:'server'}).js.code.length>100);
  assert.match(frame,/const footballCategory=\$derived\(/);
  assert.match(frame,/user\.organizations\.some\(org=>org\.id==='lefteria_fc'\)/);
  assert.match(frame,/user\.organizations\.some\(org=>org\.id==='lefteria_fc_academy'\)/);
  for(const text of ["'Ομάδες & Ακαδημία'","'Ακαδημία & τμήματα'","'Πρώτη ομάδα'"])
    assert.ok(frame.includes(text),text);
  assert.match(frame,/\{footballCategory\} <span aria-hidden="true">/);
  assert.match(frame,/adminSectionsFor\(user\)/);
  assert.match(frame,/module=>module\.status==='live'/);
  assert.doesNotMatch(frame,/fetch\(|localStorage|sessionStorage/);
});

test('player CRM deep link highlights only the selected view, without extra menus',()=>{
  assert.match(frame,/import \{ page \} from '\$app\/state'/);
  assert.match(frame,/const viewingPlayerCrm=\$derived\(active==='groups'&&page\.url\.pathname==='\/admin\/groups'&&/);
  assert.match(frame,/page\.url\.searchParams\.get\('view'\)==='players'/);
  assert.match(frame,/href="\/admin\/groups\?view=players"/);
  assert.match(frame,/aria-current=\{viewingPlayerCrm\?'page':undefined\}/);
  assert.match(frame,/\['groups','schedule','attendance','evaluations','documents','matches'\]\.includes\(active\)&&!viewingPlayerCrm/);
  assert.equal((frame.match(/class="admin-nav-disclosure"/g)||[]).length,5);
  assert.doesNotMatch(frame,/href="\/admin\/(?:schedule|matches|attendance|evaluations|documents)"/);
});

test('category type remains visibly larger than children at desktop and mobile widths',()=>{
  assert.match(css,/font:800 18px\/1\.3 'Fira Sans',sans-serif/);
  assert.match(css,/font:600 14px\/1\.4 'Manrope',sans-serif/);
  assert.match(css,/admin-nav-settings-home\)\{font-size:19px\}/);
  assert.match(css,/admin-nav-settings-home\)\{font-size:17px\}/);
  assert.match(css,/\.admin-nav-links a\{[\s\S]*?min-height:44px/);
  assert.match(css,/\.admin-nav-category:focus-visible/);
  assert.match(css,/@media\(max-width:360px\)/);
  assert.match(css,/@media\(prefers-reduced-motion:reduce\)/);
});
