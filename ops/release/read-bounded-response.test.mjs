import test from 'node:test';
import assert from 'node:assert/strict';
import {readBoundedResponse} from './read-bounded-response.mjs';

test('reads small strict UTF-8 JSON and HTML response streams safely',async()=>{
  const json=Response.json({status:'ready'});
  assert.deepEqual(JSON.parse(await readBoundedResponse(json,8192)),{status:'ready'});
  const html=new Response('<!doctype html><html></html>');
  assert.match(await readBoundedResponse(html,1024*1024),/html/);
});
test('fails closed on misleading, oversized and malformed content lengths',async()=>{
  for(const size of ['10000000','-1','NaN','1.2','00010000000']){
    const response=new Response('ok',{headers:{'content-length':size}});
    await assert.rejects(()=>readBoundedResponse(response,8192));
  }
  await assert.rejects(()=>readBoundedResponse(new Response('ok'),0));
  await assert.rejects(()=>readBoundedResponse(new Response('ok'),1024*1024+1));
});
test('does not trust a small Content-Length when streaming oversized content',async()=>{
  const oversized='x'.repeat(10_000);
  const response=new Response(oversized,{
    headers:{'content-length':'1'}
  });
  await assert.rejects(()=>readBoundedResponse(response,8192));
});
test('rejects very large chunked bodies before accepting any success status',async()=>{
  const stream=new ReadableStream({
    start(controller){
      controller.enqueue(new Uint8Array(1024*1024));
      controller.enqueue(new Uint8Array(1));
      controller.close();
    }
  });
  await assert.rejects(
    ()=>readBoundedResponse(new Response(stream),1024*1024));
});
test('rejects invalid UTF-8 and missing body without logging contents',async()=>{
  await assert.rejects(
    ()=>readBoundedResponse(new Response(new Uint8Array([0xff,0xfe])),8192));
  await assert.rejects(()=>readBoundedResponse(new Response(null),8192));
});
