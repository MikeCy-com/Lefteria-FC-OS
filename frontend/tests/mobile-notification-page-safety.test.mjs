import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const inbox=readFileSync(new URL('../src/lib/mobile/components/MobileAppNotificationInbox.svelte',import.meta.url),'utf8');

test('private inbox compiles and fetches fresh pages rather than retaining earlier private rows',()=>{
  compile(inbox,{filename:'MobileAppNotificationInbox.svelte',generate:'server'});
  compile(inbox,{filename:'MobileAppNotificationInbox.svelte',generate:'client'});
  assert.match(inbox,/busy=true;error=null;items=\[\];nextOffset=null;enabled=true;[\s\S]*?await controller\.getAppNotificationInbox\(offset\)/);
  assert.match(inbox,/if\(!alive\|\|marker!==request\)return/);
  assert.match(inbox,/items=page\.enabled\?page\.notifications:\[\]/);
  assert.match(inbox,/nextOffset=page\.enabled&&page\.next_offset!==null&&page\.next_offset<=100000/);
  assert.doesNotMatch(inbox,/items=\[\.\.\.items|localStorage|sessionStorage|@html/);
});

test('invalid cursor, disabled preferences and failed pages cannot retain old inbox history',()=>{
  assert.match(inbox,/page\.next_offset!==null&&page\.next_offset<=offset/);
  assert.match(inbox,/previousOffsets=page\.enabled\?history:\[\]/);
  assert.match(inbox,/currentOffset=page\.enabled\?offset:0/);
  assert.match(inbox,/catch\{[\s\S]*?items=\[\];nextOffset=null;previousOffsets=\[\];currentOffset=0;[\s\S]*?onUnreadChange\?\.\(0\)/);
});

test('page controls fetch previous or next offset and retry starts a fresh inbox',()=>{
  assert.match(inbox,/function refresh\(\)\{return loadPage\(0,\[\]\);\}/);
  assert.match(inbox,/return loadPage\(nextOffset,\[\.\.\.previousOffsets,currentOffset\]\)/);
  assert.match(inbox,/return loadPage\(previousOffsets\[previousOffsets\.length-1\]!,previousOffsets\.slice\(0,-1\)\)/);
  assert.match(inbox,/aria-label="Σελίδες ειδοποιήσεων"/);
  assert.match(inbox,/disabled=\{!previousOffsets\.length\}/);
  assert.match(inbox,/disabled=\{nextOffset===null\}/);
  assert.match(inbox,/class="outline retry" onclick=\{\(\)=>void refresh\(\)\}/);
});
