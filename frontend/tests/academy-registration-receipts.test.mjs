import test from 'node:test';
import assert from 'node:assert/strict';
import {parseAcademyRegistrationReceipts}
  from '../src/lib/academy/registration-receipts.ts';

const ids=Array.from({length:5},(_,i)=>String(i+1).padStart(32,'0'));
const result=receipts=>({receipt:receipts[0],receipts});
test('accepts one through five distinct well-formed committed receipt identifiers',()=>{
  for(let n=1;n<=5;n++){
    const list=ids.slice(0,n);
    assert.deepEqual(parseAcademyRegistrationReceipts(result(list)),result(list));
  }
});
test('never confirms two children under a duplicate receipt',()=>{
  assert.equal(parseAcademyRegistrationReceipts(result([ids[0],ids[0]])),null);
});
test('rejects mismatched first receipt, opaque content, invalid shape and excess children',()=>{
  for(const payload of [
    null,{},[],{receipt:ids[0],receipts:[]},
    {receipt:ids[0],receipts:ids.concat(ids[0])},
    {receipt:ids[4],receipts:[ids[0],ids[1]]},
    {receipt:ids[0].slice(0,31)+'A',receipts:[ids[0]]},
    {receipt:'x'.repeat(32),receipts:['x'.repeat(32)]},
    {receipt:ids[0],receipts:[ids[0],false]},
    {receipt:ids[0],receipts:'not an array'}
  ])assert.equal(parseAcademyRegistrationReceipts(payload),null);
});

test('rejects sparse receipt arrays even when the first identifier is valid',()=>{
  const missingSecond=[ids[0],,];
  assert.equal(missingSecond.length,2);
  assert.equal(parseAcademyRegistrationReceipts(result(missingSecond)),null);
  const missingMiddle=[ids[0],,ids[2]];
  assert.equal(parseAcademyRegistrationReceipts(result(missingMiddle)),null);
});
