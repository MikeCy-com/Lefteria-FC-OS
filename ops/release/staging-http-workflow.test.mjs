import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const yaml=readFileSync(
  new URL('../../.github/workflows/staging-http-preflight.yml',import.meta.url),
  'utf8');

test('staging HTTP workflow only runs after manual G0 acknowledgement',()=>{
  assert.match(yaml,/workflow_dispatch:/);
  assert.match(yaml,/isolation_confirmed:/);
  assert.match(yaml,/ISOLATION_CONFIRMED/);
  assert.match(yaml,/G0_TICKET/);
  assert.match(yaml,/git rev-parse HEAD/);
  assert.doesNotMatch(yaml,/^\s*(?:push|schedule|workflow_run|pull_request):/m);
});
test('staging HTTP job uses only read permissions and exact-main CI evidence',()=>{
  assert.match(yaml,/permissions:\s*\n\s*contents: read\s*\n\s*actions: read/);
  assert.match(yaml,/verify-exact-ci[.]mjs/);
  assert.match(yaml,/staging-mvp-preflight[.]mjs/);
  assert.match(yaml,/persist-credentials: false/);
  assert.doesNotMatch(yaml,/^\s*environment:\s*(?:production|live)\s*$/m);
});
test('no provider credentials, remote mutation or automatic deployment steps',()=>{
  assert.doesNotMatch(yaml,/secrets\.[\w-]*(?:HOSTINGER|PAYABL|TWILIO|SMTP|DB_PASSWORD)/i);
  assert.doesNotMatch(yaml,/^\s*run:\s*(?:ssh|scp|docker compose up|kubectl|helm|psql|gh release)/m);
  assert.match(yaml,/GET-only HTTP preflight/);
});
