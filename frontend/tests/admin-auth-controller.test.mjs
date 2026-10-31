import test from 'node:test';
import assert from 'node:assert/strict';
import { createAdminAuthController } from '../src/lib/admin/controller.ts';

const NOW = Date.parse('2026-10-31T20:00:00Z');
function session(patch = {}) {
  return { user: { id: '11111111-1111-4111-8111-111111111111', username: 'cms-fixture', name: 'Διαχειριστής δοκιμής',
    role: 'manager', permissions: ['news:*', 'teams:*'], organizations: [{ id: 'lefteria_fc', name: 'Lefteria FC' }], ...patch },
    expires_at: '2026-11-01T20:00:00Z' };
}

function setup(handler) {
  const requests = [];
  const controller = createAdminAuthController({ baseUrl: 'https://club.example', now: () => NOW, fetch: async request => {
    const item = { path: new URL(request.url).pathname, method: request.method, credentials: request.credentials,
      cache: request.cache, authorization: request.headers.get('Authorization'), origin: request.headers.get('Origin'),
      marker: request.headers.get('X-Lefteria-Admin'), contentType: request.headers.get('Content-Type'),
      body: request.body === null ? undefined : await request.clone().json() };
    requests.push(item);
    return handler(item, requests);
  } });
  return { controller, requests };
}

test('CMS login preserves the exact password and username and uses same-origin cookies with the CSRF contract', async () => {
  const { controller, requests } = setup(() => Response.json(session()));
  assert.equal(await controller.login(' Cms Name ', ' κωδικός '.repeat(15), 60), true);
  assert.deepEqual(requests, [{ path: '/api/admin/auth/login', method: 'POST', credentials: 'same-origin', cache: 'no-store',
    authorization: null, origin: null, marker: '1', contentType: 'application/json',
    body: { username: ' Cms Name ', password: ' κωδικός '.repeat(15), remember_days: 60 } }]);
  assert.equal(controller.getState().status, 'authenticated');
  assert.equal('token' in controller.getState(), false);
});

test('CMS remember choices retain explicit null and each supported duration', async () => {
  for (const days of [null, 30, 60, 90]) {
    const { controller, requests } = setup(() => Response.json(session()));
    await controller.login('cms-fixture', 'fixture password', days);
    assert.equal(requests[0].body.remember_days, days);
  }
});

test('CMS me replaces current grants and exposes both assigned organizations without selecting a mobile context', async () => {
  const latest = session({ permissions: ['news:read'], organizations: [
    { id: 'lefteria_fc', name: 'Lefteria FC' }, { id: 'lefteria_fc_academy', name: 'Lefteria FC Academy' }] });
  const { controller, requests } = setup(({ method }) => Response.json(method === 'POST' ? session() : latest));
  await controller.login('cms-fixture', 'fixture password', null);
  assert.equal(await controller.bootstrap(), true);
  assert.deepEqual(controller.getState().session, latest);
  assert.equal(requests[1].credentials, 'same-origin');
  assert.equal(requests[1].authorization, null);
  assert.equal(requests[1].cache, 'no-store');
  assert.deepEqual(requests.map(request => request.path), ['/api/admin/auth/login', '/api/admin/auth/me']);
});

test('CMS me 401 clears identity, permissions, organizations and session expiry together', async () => {
  const { controller } = setup(({ method }) => method === 'POST' ? Response.json(session()) :
    Response.json({ detail: 'Session expired', code: 'not_authenticated' }, { status: 401 }));
  await controller.login('cms-fixture', 'fixture password', null);
  assert.equal(await controller.bootstrap(), false);
  assert.deepEqual(controller.getState(), { status: 'anonymous', session: null, busy: false, error: null });
});

test('CMS 2FA accounts fail closed and never call a provider or create a substitute challenge', async () => {
  const { controller, requests } = setup(() => Response.json({ detail: 'Provider private detail', code: 'two_factor_unavailable' }, { status: 403 }));
  assert.equal(await controller.login('cms-fixture', 'fixture password', null), false);
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(controller.getState().session, null);
  assert.match(controller.getState().error, /2FA/);
  assert.equal(controller.getState().error.includes('Provider private detail'), false);
  assert.equal(requests.length, 1);
});

test('CMS unknown roles and invalid session payloads never restore access', async () => {
  for (const data of [session({ role: 'admin' }), session({ role: 'toString' }), session({ organizations: [{ id: 'other', name: 'Other' }] }),
    { ...session(), expires_at: 'invalid' }, { ...session(), expires_at: '2026-10-31T19:00:00Z' }]) {
    const { controller } = setup(() => Response.json(data));
    assert.equal(await controller.bootstrap(), false);
    assert.equal(controller.getState().session, null);
    assert.notEqual(controller.getState().status, 'authenticated');
  }
});

test('CMS network failure hides previous private data without displaying exception or response details', async () => {
  const { controller } = setup(({ method }) => {
    if (method === 'POST') return Response.json(session());
    throw new TypeError('PRIVATE-provider-password');
  });
  await controller.login('cms-fixture', 'fixture password', null);
  assert.equal(await controller.bootstrap(), false);
  assert.equal(controller.getState().status, 'unavailable');
  assert.equal(controller.getState().session, null);
  assert.equal(controller.getState().error.includes('PRIVATE'), false);
});

test('CMS logout confirms revocation with JSON request headers and clears all state', async () => {
  const { controller, requests } = setup(({ path }) => path.endsWith('/logout') ? new Response(null, { status: 204 }) : Response.json(session()));
  await controller.bootstrap();
  assert.equal(await controller.logout(), true);
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(controller.getState().session, null);
  const request = requests.at(-1);
  assert.equal(request.path, '/api/admin/auth/logout');
  assert.equal(request.credentials, 'same-origin');
  assert.equal(request.marker, '1');
  assert.equal(request.contentType, 'application/json');
  assert.equal(request.authorization, null);
});

test('CMS logout failure is explicit and retry can confirm server logout', async () => {
  let fail = true;
  const { controller } = setup(({ path }) => {
    if (!path.endsWith('/logout')) return Response.json(session());
    return fail ? Response.json({ detail: 'private' }, { status: 503 }) : new Response(null, { status: 204 });
  });
  await controller.bootstrap();
  assert.equal(await controller.logout(), false);
  assert.equal(controller.getState().status, 'unavailable');
  assert.equal(controller.getState().session, null);
  assert.match(controller.getState().error, /αποσύνδεση/);
  fail = false;
  assert.equal(await controller.logout(), true);
  assert.equal(controller.getState().status, 'anonymous');
});

test('a delayed CMS me response cannot restore a logged-out session', async () => {
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const { controller } = setup(({ path }) => path.endsWith('/me') ? pending : new Response(null, { status: 204 }));
  const reading = controller.bootstrap();
  await controller.logout();
  release(Response.json(session()));
  await reading;
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(controller.getState().session, null);
});

test('CMS absolute expiry clears all identity state and disposed controllers ignore pending results', async () => {
  let time = NOW;
  const controller = createAdminAuthController({ baseUrl: 'https://club.example', now: () => time,
    fetch: async () => Response.json(session()) });
  await controller.bootstrap();
  time += 86_400_000;
  controller.expireIfNeeded();
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(controller.getState().session, null);
  controller.dispose();
  assert.equal(await controller.bootstrap(), false);
});
