import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('../src/routes/api/public/academy/registration/[registrationId]/documents/+server.ts',import.meta.url),'utf8');

test('upstream 429 maps to an explicit generic rate-limit code without response disclosure',()=>{
  assert.match(source,/response\.status===429\?'upload_rate_limited':/);
  assert.match(source,/\[400,403,409,413,415,422,429\]\.includes\(response\.status\)/);
  assert.match(source,/return json\(\{code\},\{status,headers\}\)/);
  assert.doesNotMatch(source,/response\.text\(/);
});
