import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const canvas=read('lib/admin/WebsiteVisualCanvas.svelte');
const editor=read('routes/admin/website-pages/+page.svelte');

test('new visual canvas and existing canonical page editor both compile',()=>{
  for(const [name,source] of [['WebsiteVisualCanvas',canvas],['website-pages',editor]]){
    const result=compile(source,{filename:name+'.svelte',generate:'server'});
    assert.ok(result.js.code.length>100,name);
  }
});
test('editor can click text directly on the canvas, drag and insert sections at any position',()=>{
  assert.match(canvas,/data-testid="website-visual-canvas"/);
  assert.match(canvas,/data-testid="website-visual-inline-text"/);
  assert.match(canvas,/ondragstart=\{e=>start\(e,index\)\}/);
  assert.match(canvas,/ondrop=\{e=>drop\(e,index\)\}/);
  assert.match(canvas,/onInsert\(index,kind\.kind\)/);
  assert.match(editor,/function insertBlock\(index:number,type:WebsiteBlock\['type'\]\)/);
  assert.match(editor,/function moveCanvasBlock\(/);
  assert.match(editor,/onEdit=\{editCanvasText\}/);
  assert.match(editor,/onMove=\{moveCanvasBlock\}/);
  assert.match(editor,/data-testid="cms-canonical-pages"/);
  assert.match(editor,/\/admin\/club/);
  assert.match(editor,/\/admin\/news/);
});
test('visual editing stays a private draft with publication and grant checks intact',()=>{
  assert.match(editor,/hasAdminPermission\(\$auth\.session\.user\.permissions,'news:write'\)/);
  assert.match(editor,/!canPublish \|\| dirty \|\| saving/);
  assert.match(editor,/function save\(/);
  assert.match(editor,/expected_version:selected\.version,publish/);
  assert.match(editor,/!canWrite\|\|saving\|\|isLockedRegion/);
  assert.doesNotMatch(canvas,/\{@html\}|innerHTML|localStorage|sessionStorage/);
});
