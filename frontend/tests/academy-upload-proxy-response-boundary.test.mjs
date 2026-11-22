import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(
  new URL('../src/routes/api/public/academy/registration/[registrationId]/documents/+server.ts',import.meta.url),
  'utf8'
);

test('public document relay does not forward browser credentials or expose upstream payloads',()=>{
  assert.match(source,/credentials:'omit'/);
  assert.match(source,/headers:\{origin:url\.origin,'content-type':contentType\}/);
  assert.match(source,/cache:'no-store'/);
  assert.doesNotMatch(source,/request\.headers\.get\(['"](?:cookie|authorization)['"]\)/i);
  assert.doesNotMatch(source,/return\s+response\b|return\s+json\(result\b|return\s+json\(payload\b/);
});

test('successful relay responses expose only an allowlisted slot, quarantine status and replay flag',()=>{
  assert.match(source,/response\.status===202/);
  assert.match(source,/\['id_front','id_back','birth_certificate','passport_photo'\]\.includes/);
  assert.match(source,/payload\.status==='quarantined'/);
  assert.match(source,/typeof payload\.replay==='boolean'/);
  assert.match(source,/return json\(\{slot:payload\.slot,status:'quarantined',\s*replay:payload\.replay\},\{status:202,headers\}\)/);
  assert.match(source,/return json\(\{code\},\{status,headers\}\)/);
  assert.match(source,/return json\(\{code:'upload_unavailable'\},\{status:503,headers\}\)/);
});

test('relay rejects cross-origin requests and bounds streamed uploads independently of content length',()=>{
  assert.match(source,/request\.headers\.get\('origin'\)!==url\.origin/);
  assert.match(source,/\^\[0-9a-f\]\{32\}\$/);
  assert.match(source,/size\+=value\.byteLength/);
  assert.match(source,/if\(size>11\*1024\*1024\)/);
  assert.match(source,/AbortSignal\.timeout\(30000\)/);
});

test('relay rejects malformed declared lengths before reading a body or contacting upstream',()=>{
  assert.match(source,/const declaredLength=request\.headers\.get\('content-length'\)/);
  assert.match(source,/if\(declaredLength!==null\)/);
  assert.match(source,/if\(!\/\^\(0\|\[1-9\]\[0-9\]\*\)\$\/\.test\(declaredLength\)\)/);
  assert.match(source,/return json\(\{code:'invalid_upload'\},\{status:400,headers\}\)/);
  assert.match(source,/if\(Number\(declaredLength\)>11\*1024\*1024\)/);
  assert.ok(source.indexOf('const declaredLength=')<source.indexOf('request.body?.getReader()'));
});
