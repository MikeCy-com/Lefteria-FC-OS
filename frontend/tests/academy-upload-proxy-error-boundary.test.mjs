import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(
  new URL('../src/routes/api/public/academy/registration/[registrationId]/documents/+server.ts', import.meta.url),
  'utf8'
);

test('document relay fails closed for upstream errors without reflecting response bodies', () => {
  assert.match(source, /const code=response\.status===403\?'upload_capability_invalid'/);
  assert.match(source, /response\.status===409\?'document_slot_conflict'/);
  assert.match(source, /response\.status===413\?'file_too_large'/);
  assert.match(source, /response\.status===422\?'invalid_upload':'upload_unavailable'/);
  assert.match(source, /\[400,403,409,413,415,422,429\]\.includes\(response\.status\)/);
  assert.match(source, /\?response\.status:503/);
  assert.doesNotMatch(source, /response\.text\(/);
  assert.doesNotMatch(source, /return\s+response\b/);
});

test('document relay applies private no-store response headers before all rejection paths', () => {
  const headerIndex = source.indexOf("const headers={'cache-control':'private, no-store',pragma:'no-cache'}");
  const originGuard = source.indexOf("request.headers.get('origin')!==url.origin");
  const upstreamFetch = source.indexOf('const response=await fetch(');
  assert.ok(headerIndex >= 0, 'private cache headers must be defined');
  assert.ok(originGuard > headerIndex, 'origin denial must use private headers');
  assert.ok(upstreamFetch > originGuard, 'origin denial must precede upstream contact');
  assert.match(source, /return json\(\{code:'origin_denied'\},\{status:403,headers\}\)/);
  assert.match(source, /return json\(\{code:'upload_unavailable'\},\{status:503,headers\}\)/);
});
