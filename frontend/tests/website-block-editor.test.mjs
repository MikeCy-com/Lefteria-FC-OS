import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { moveWebsiteBlock, duplicateWebsiteBlock } from '../src/lib/admin/website-block-editor.ts';

const blocks = [
  {type:'heading',text:'One',url:null},
  {type:'paragraph',text:'Two',url:null},
  {type:'image',text:'Three',url:'https://example.org/three.png'}
];

test('drag move uses insertion ordering and leaves draft source intact',()=>{
  const reordered=moveWebsiteBlock(blocks,0,2);
  assert.deepEqual(reordered.map(x=>x.text),['Two','Three','One']);
  assert.deepEqual(moveWebsiteBlock(blocks,2,0).map(x=>x.text),['Three','One','Two']);
  assert.deepEqual(moveWebsiteBlock(blocks,1,1),blocks);
  assert.deepEqual(blocks.map(x=>x.text),['One','Two','Three']);
  assert.equal(reordered[2],blocks[0]);
});

test('invalid and cross-boundary moves never delete, clone or reorder content',()=>{
  for(const [from,to] of [[-1,0],[1,3],[3,0],[NaN,0],[0,Infinity],[0,1.5]]){
    const out=moveWebsiteBlock(blocks,from,to);
    assert.deepEqual(out,blocks);
    assert.notEqual(out,blocks);
  }
});

test('duplication preserves block content independently and never exceeds the limit',()=>{
  const next=duplicateWebsiteBlock(blocks,2);
  assert.equal(next.length,4);
  assert.deepEqual(next[3],blocks[2]);
  assert.notEqual(next[3],blocks[2]);
  next[3].text='Edited duplicate';
  assert.equal(blocks[2].text,'Three');
  assert.deepEqual(duplicateWebsiteBlock(blocks,-1),blocks);
  assert.equal(duplicateWebsiteBlock(Array.from({length:40},()=>blocks[0]),0).length,40);
});

test('editor exposes accessible drag, arrow and duplication controls with guarded permissions',()=>{
  const editor=readFileSync(new URL('../src/routes/admin/website-pages/+page.svelte',import.meta.url),'utf8');
  assert.match(editor,/on(dragstart|dragend|dragover|drop)=/);
  assert.match(editor,/draggingBlock===null \|\| !canWrite \|\| saving \|\| isLockedRegion/);
  assert.match(editor,/Σύρετε το block για αναδιάταξη/);
  assert.match(editor,/Αντιγραφή block/);
  assert.match(editor,/Μετακίνηση πάνω/);
  assert.match(editor,/Μετακίνηση κάτω/);
  assert.match(editor,/aria-live="polite"/);
  assert.match(editor,/\{#each draft\.blocks as block, index \(block\)\}/);
});
