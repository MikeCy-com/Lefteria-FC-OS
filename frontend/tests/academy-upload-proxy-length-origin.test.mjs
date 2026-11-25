import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

// Static tripwire only: integration tests must separately exercise the real relay.
const source = readFileSync(
  new URL('../src/routes/api/public/academy/registration/[registrationId]/documents/+server.ts', import.meta.url),
  'utf8'
);

test('declared upload length is checked before body consumption and upstream contact', () => {
  const lengthCheck = source.indexOf("const declaredLength=request.headers.get('content-length')");
  const reader = source.indexOf('request.body?.getReader()');
  const upstream = source.indexOf('const response=await fetch(');
  assert.ok(lengthCheck >= 0, 'declared length must be checked');
  assert.ok(reader > lengthCheck, 'length validation must precede stream reads');
  assert.ok(upstream > reader, 'upstream contact must follow bounded reads');
  assert.match(source, /\^\(0\|\[1-9\]\[0-9\]\*\)\$/);
  assert.match(source, /Number\(declaredLength\)>11\*1024\*1024/);
  assert.match(source, /size!==Number\(declaredLength\)/);
});

test('private upstream URL disallows embedded credentials and non-root paths', () => {
  assert.match(source, /\['http:','https:'\]\.includes\(apiOrigin\.protocol\)/);
  assert.match(source, /apiOrigin\.username\|\|apiOrigin\.password/);
  assert.match(source, /apiOrigin\.pathname!=='\/'/);
  assert.match(source, /apiOrigin\.search\|\|apiOrigin\.hash/);
  assert.match(source, /credentials:'omit',cache:'no-store',redirect:'error'/);
});
