import test from 'node:test';
import assert from 'node:assert/strict';
import { lockPageScroll } from '../src/lib/public/dialog-scroll.ts';

test('public modal scroll lock restores prior inline values/priorities after the final owner and tolerates repeated cleanup', () => {
  const values = new Map([['overflow',['auto','important']],['padding-right',['5px','']]]);
  const style = { getPropertyValue:key=>values.get(key)?.[0]??'',getPropertyPriority:key=>values.get(key)?.[1]??'',
    setProperty:(key,value,priority)=>values.set(key,[value,priority]),removeProperty:key=>values.delete(key) };
  const document = {body:{style},documentElement:{clientWidth:1000},defaultView:{innerWidth:1015,getComputedStyle:()=>({paddingRight:'5px'})}};
  const first = lockPageScroll(document); const second = lockPageScroll(document);
  assert.deepEqual(values.get('overflow'),['hidden','important']); assert.deepEqual(values.get('padding-right'),['20px','important']);
  first();first(); assert.deepEqual(values.get('overflow'),['hidden','important']);
  second(); assert.deepEqual(values.get('overflow'),['auto','important']); assert.deepEqual(values.get('padding-right'),['5px','']);
  const emptyStyle = {getPropertyValue:()=>'',getPropertyPriority:()=>'',setProperty:(key,value,priority)=>values.set(key,[value,priority]),removeProperty:key=>values.delete(key)};
  const empty = {body:{style:emptyStyle},documentElement:{clientWidth:1000},defaultView:{innerWidth:1000,getComputedStyle:()=>({paddingRight:'0px'})}};
  const release = lockPageScroll(empty);release(); assert.equal(values.has('overflow'),false); assert.equal(values.has('padding-right'),false);
});
