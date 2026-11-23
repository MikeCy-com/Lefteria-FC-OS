import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(
  new URL('../src/routes/api/public/academy/registration/[registrationId]/documents/+server.ts',import.meta.url),
  'utf8'
);

test('multipart content type is bounded and checked before body access or upstream fetch',()=>{
  assert.match(source,/contentType\.startsWith\('multipart\/form-data; boundary='\)/);
  assert.match(source,/contentType\.slice\('multipart\/form-data; boundary='\.length\)\.trim\(\)/);
  assert.match(source,/contentType\.length>200/);
  assert.match(source,/\[\\r\\n\]\.test\(contentType\)/);
  const typeGuard=source.indexOf("const contentType=request.headers.get('content-type')");
  const reader=source.indexOf('request.body?.getReader()');
  const upstream=source.indexOf('const response=await fetch(');
  assert.ok(typeGuard>=0&&reader>typeGuard&&upstream>reader);
  assert.match(source.slice(typeGuard,reader),/return json\(\{code:'multipart_required'\},\{status:415,headers\}\)/);
});

test('missing request stream is rejected without an upstream request',()=>{
  assert.match(source,/if\(!reader\)return json\(\{code:'invalid_upload'\},\{status:400,headers\}\)/);
  assert.ok(source.indexOf('if(!reader)')<source.indexOf('const response=await fetch('));
});
