import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Source-level regression guard; not a substitute for an isolated HTTP test.
const source = readFileSync(
  new URL('../src/routes/api/public/academy/registration/[registrationId]/documents/+server.ts', import.meta.url),
  'utf8'
);

test('upstream rate limiting never returns upstream response content or a success status', () => {
  assert.match(source, /const status=\[400,403,409,413,415,422,429\]\.includes\(response\.status\)/);
  assert.match(source, /\?response\.status:503/);
  assert.match(source, /return json\(\{code\},\{status,headers\}\)/);
  assert.doesNotMatch(source, /return\s+(?:new\s+)?Response\s*\(\s*await response\.(?:text|json|arrayBuffer)/);
});

test('all relay outcomes retain private no-store response headers', () => {
  assert.match(source, /const headers=\{'cache-control':'private, no-store',pragma:'no-cache'\}/);
  assert.match(source, /return json\(\{code:'origin_denied'\},\{status:403,headers\}\)/);
  assert.match(source, /return json\(\{code:'upload_unavailable'\},\{status:503,headers\}\)/);
  assert.match(source, /return json\(\{slot:payload\.slot,status:'quarantined',\s*replay:payload\.replay\},\{status:202,headers\}\)/);
});
