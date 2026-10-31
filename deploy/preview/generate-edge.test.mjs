import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { bearerRouteGroups, generateEdge, protectedOperations, publicWebExpression, publicApiExpression, reviewedPublicReads } from './generate-edge.mjs';

const contract = JSON.parse(await readFile(new URL('../../contracts/openapi.json', import.meta.url), 'utf8'));
const hostname = 'srv1649602.hstgr.cloud';
const digest = 'a'.repeat(64);
const permits = (document, method, path) => bearerRouteGroups(document).some(group =>
  group.method === method && new RegExp(group.expression).test(path));

test('only the twelve explicit Mobile operations receive Bearer routing', () => {
  const operations = protectedOperations(contract);
  assert.equal(operations.length, 12);
  for (const operation of operations) {
    const path = operation.path.replace(/\{[^}]+\}/g, 'record');
    assert.ok(permits(contract, operation.method, path), `${operation.method} ${path}`);
    assert.equal(permits(contract, 'OPTIONS', path), false);
    assert.equal(permits(contract, operation.method, path + '/extra'), false);
  }
  for (const path of [
    '/api/mobile/auth/request-otp', '/api/mobile/auth/verify-otp', '/api/mobile/auth/register',
    '/api/mobile/auth/exchange-legacy', '/api/health/live', '/api/uploads/avatars/photo.jpg',
    '/api/mobile/future-public-route', '/app', '/api/mobile/calendar/event/id/responses/extra',
    '/api/mobile/calendar/event/nested/id/responses', '/api/mobile/calendar//id/responses'
  ]) {
    for (const method of ['GET', 'POST', 'PUT']) assert.equal(permits(contract, method, path), false, `${method} ${path}`);
  }
  assert.equal(permits(contract, 'POST', '/api/mobile/calendar'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/profile'), false);
});

test('anonymous and newly declared non-Mobile operations remain gated', () => {
  const changed = structuredClone(contract);
  changed.paths['/api/mobile/anonymous'] = { get: { security: [] } };
  changed.paths['/api/mobile/unmarked'] = { post: {} };
  changed.paths['/api/mobile/auth/exchange-legacy'].post.security = [{ LegacyMobile: [] }];
  assert.equal(protectedOperations(changed).length, 12);
  assert.equal(permits(changed, 'GET', '/api/mobile/anonymous'), false);
  assert.equal(permits(changed, 'POST', '/api/mobile/unmarked'), false);
});

test('ambiguous security, global defaults and unsafe templates fail closed', () => {
  for (const security of [[{ Mobile: [] }, {}], [{ Mobile: [], Other: [] }], [{ Mobile: ['scope'] }]]) {
    const changed = structuredClone(contract);
    changed.paths['/api/mobile/calendar'].get.security = security;
    assert.throws(() => protectedOperations(changed), /Ambiguous/);
  }
  assert.throws(() => protectedOperations({ ...contract, security: [{ Mobile: [] }] }), /Explicit/);
  for (const path of ['/outside', '/api/mobile/a*', '/api/mobile/a.b', '/api/mobile/a//b', '/api/mobile/{unknown}', '/api/mobile/{part}suffix']) {
    const changed = structuredClone(contract);
    changed.paths[path] = { get: { security: [{ Mobile: [] }] } };
    assert.throws(() => protectedOperations(changed));
  }
});

test('generated edge separates Bearer routing from the hashed Basic fallback', () => {
  const output = generateEdge(contract, hostname, digest);
  assert.match(output, /https:\/\/srv1649602\.hstgr\.cloud:8443/);
  assert.match(output, /auto_https off/);
  assert.match(output, /tls \/certificates\/current\/fullchain\.pem \/certificates\/current\/privkey\.pem/);
  assert.match(output, /basic_auth bcrypt "Lefteria OS preview"/);
  assert.match(output, /preview \{env\.PREVIEW_GATE_HASH\}/);
  assert.equal((output.match(/header_up -Authorization/g) ?? []).length, 4);
  assert.equal((output.match(/header_up -Cookie/g) ?? []).length, 2);
  assert.equal((output.match(/header_regexp bearer_/g) ?? []).length, bearerRouteGroups(contract).length);
  assert.ok(output.indexOf('handle @private_') < output.indexOf('\n      basic_auth'));
  assert.doesNotMatch(output, /listen (?:80|443)\b|:80\s*\{|:443\s*\{|tls internal|on_demand/);
  assert.match(output, /X-Robots-Tag "noindex, nofollow, noarchive"/);
  assert.match(output, /Cache-Control "no-store"/);
  assert.throws(() => generateEdge(contract, 'lefteriafc.cy', digest), /hostname/);
  assert.throws(() => generateEdge(contract, hostname, 'invalid'), /SHA-256/);
});

test('the public website and only reviewed public API reads avoid the preview gate', () => {
  assert.equal(reviewedPublicReads(contract).length, 3);
  const web = new RegExp(publicWebExpression), api = new RegExp(publicApiExpression);
  for (const path of ['/', '/__data.json', '/team', '/academy/__data.json', '/news', '/news/story--id', '/news/story--id/__data.json', '/_app/immutable/chunks/part.js', '/club/logo512.png', '/mobile/club-logo.png', '/favicon.ico']) assert.ok(web.test(path), path);
  for (const path of ['/admin', '/admin/login', '/admin/__data.json', '/app', '/app/login', '/api/admin/auth/me', '/_app/../admin', '/api/mobile/auth/me', '/news/id/extra']) assert.equal(web.test(path), false, path);
  for (const path of ['/api/public/site', '/api/public/news', '/api/public/news/id']) assert.ok(api.test(path), path);
  for (const path of ['/api/public/new-route', '/api/public/news/id/extra', '/api/admin/auth/login', '/api/mobile/profile', '/api/health/ready']) assert.equal(api.test(path), false, path);
  const changed = structuredClone(contract);
  changed.paths['/api/public/site'].get.security = [{ WebAdmin: [] }];
  assert.throws(() => generateEdge(changed, hostname, digest), /anonymous/);
  const output = generateEdge(contract, hostname, digest);
  assert.ok(output.indexOf('handle @public_web') < output.indexOf('\n      basic_auth'));
  assert.match(output, /@public_api \{\n    method GET\n/);
  assert.match(output, /@legacy_upload[\s\S]*handle @legacy_upload[\s\S]*respond "" 404/);
});

test('error routes apply the same security headers without replacing status or authentication challenges', () => {
  const output = generateEdge(contract, hostname, digest);
  const errorRoute = output.split('  handle_errors {\n')[1]?.split('\n  }')[0];
  assert.ok(errorRoute, 'Authentication errors need their own header handler.');
  for (const line of [
    'X-Frame-Options DENY', 'X-Content-Type-Options nosniff', 'Referrer-Policy no-referrer',
    'X-Robots-Tag "noindex, nofollow, noarchive"', 'Cache-Control "no-store"', '-Server'
  ]) {
    assert.ok(errorRoute.includes(line));
    assert.equal(output.split(line).length - 1, 2, 'Normal and error response headers must agree.');
  }
  assert.ok(errorRoute.includes('respond "" {err.status_code}'));
  assert.doesNotMatch(errorRoute, /WWW-Authenticate|err\.(?:message|trace|id)|reverse_proxy|respond[^\n]*200/i);
});
