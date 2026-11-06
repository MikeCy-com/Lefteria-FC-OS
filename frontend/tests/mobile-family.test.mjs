import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';

const identityId = '11111111-1111-4111-8111-111111111111';
function user(role = 'parent') {
  return { id: identityId, phone: '+35799123456', role, name: 'Synthetic role',
    platform: 'Web/PWA', created_at: '2026-01-01T00:00:00Z', last_login: '2026-11-01T00:00:00Z',
    current_role_available: true, available_roles: [role, 'fan'] };
}
const family = {
  children: [
    { id: 'child-academy', name: 'Παιδί Άλφα', organization_id: 'lefteria_fc_academy',
      team_type: 'Academy', team_id: null, academy_group_ids: ['academy-group'] },
    { id: 'child-club', name: 'Παιδί Βήτα', organization_id: 'lefteria_fc',
      team_type: 'First Team', team_id: 'club-team', academy_group_ids: [] }
  ]
};

function setup(handler, role = 'parent') {
  const items = new Map([['mobile_token', role + '-test-token']]);
  const storage = createMobileSessionStorage({
    getItem: key => items.get(key) ?? null,
    setItem: (key, value) => items.set(key, value),
    removeItem: key => items.delete(key)
  });
  const calls = [];
  const controller = createMobileAuthController({
    baseUrl: 'https://club.example', storage,
    fetch: async request => {
      const call = { path: new URL(request.url).pathname, method: request.method,
        authorization: request.headers.get('Authorization'), cache: request.cache };
      calls.push(call);
      return await handler(call);
    }
  });
  return { controller, calls, items };
}

test('only a current parent can load guardian-linked children through bearer API', async () => {
  const { controller, calls } = setup(({ path }) => {
    if (path === '/api/mobile/auth/me') return Response.json(user());
    if (path === '/api/mobile/family') return Response.json(family);
    throw Error('Unexpected API path ' + path);
  });
  await controller.bootstrap();
  assert.equal(controller.getState().familyStatus, 'idle');
  assert.equal(await controller.loadFamily(), true);
  assert.deepEqual(controller.getState().family, family);
  assert.equal(controller.getState().familyStatus, 'ready');
  assert.deepEqual(calls.at(-1), { path: '/api/mobile/family', method: 'GET',
    authorization: 'Bearer parent-test-token', cache: 'no-store' });
  controller.logout();
  assert.equal(controller.getState().family, null);
  assert.equal(controller.getState().familyStatus, 'idle');
  assert.equal(controller.getState().status, 'anonymous');
  for (const role of ['fan', 'coach', 'management', 'player']) {
    const other = setup(({ path }) => path === '/api/mobile/auth/me' ? Response.json(user(role))
      : (() => { throw Error('Private family endpoint accessed by nonparent'); })(), role);
    await other.controller.bootstrap();
    const count = other.calls.length;
    assert.equal(await other.controller.loadFamily(), false);
    assert.equal(other.calls.length, count);
    assert.equal(other.controller.getState().family, null);
  }
});

test('fresh revocation or failed response clears earlier private family details', async () => {
  let revoked = false;
  const { controller } = setup(({ path }) => {
    if (path === '/api/mobile/auth/me') return Response.json(user());
    if (path === '/api/mobile/family')
      return revoked ? Response.json({ detail: 'Current permission unavailable', code: 'role_unavailable' }, { status: 403 })
        : Response.json(family);
  });
  await controller.bootstrap();
  assert.equal(await controller.loadFamily(), true);
  revoked = true;
  assert.equal(await controller.loadFamily(), false);
  assert.equal(controller.getState().family, null);
  assert.equal(controller.getState().familyStatus, 'error');
  assert.ok(controller.getState().familyError);
});

test('malformed, cross-organization or duplicate records never populate the parent view', async () => {
  for (const bad of [
    { children: null }, { children: [{ ...family.children[0], organization_id: 'unknown' }] },
    { children: [{ ...family.children[0], academy_group_ids: null }] },
    { children: [{ ...family.children[0], name: '' }] },
    { children: [family.children[0], family.children[0]] }
  ]) {
    const { controller } = setup(({ path }) => path === '/api/mobile/auth/me'
      ? Response.json(user()) : Response.json(bad));
    await controller.bootstrap();
    assert.equal(await controller.loadFamily(), false);
    assert.equal(controller.getState().family, null);
    assert.equal(controller.getState().familyStatus, 'error');
  }
});

test('late family response after logout cannot restore private children', async () => {
  const pending = Promise.withResolvers();
  const { controller } = setup(({ path }) => path === '/api/mobile/auth/me'
    ? Response.json(user()) : pending.promise);
  await controller.bootstrap();
  const loading = controller.loadFamily();
  assert.equal(controller.getState().familyStatus, 'loading');
  controller.logout();
  pending.resolve(Response.json(family));
  assert.equal(await loading, false);
  assert.equal(controller.getState().family, null);
  assert.equal(controller.getState().status, 'anonymous');
});

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.endsWith('.css'))
      return {url:'data:text/javascript,export%20default%20%7B%7D',shortCircuit:true};
    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (url.endsWith('.svelte')) {
      const path = fileURLToPath(url);
      return { format:'module', source:compile(readFileSync(path,'utf8'), {filename:path,generate:'server'}).js.code,
        shortCircuit:true };
    }
    return nextLoad(url,context);
  }
});

test('actual Svelte SSR differentiates empty, unavailable and role-scoped linked-children data', async () => {
  const { default: MobileFamily } = await import('../src/lib/mobile/components/MobileFamily.svelte');
  const props = {onLoad:() => true,onCalendar:() => {} ,busy:false,error:null};
  const loaded = render(MobileFamily, {props:{...props,family,status:'ready'}}).body;
  assert.match(loaded, /data-testid="mobile-family"/);
  assert.match(loaded, /data-testid="family-children"/);
  assert.match(loaded, /Παιδί Άλφα/);
  assert.match(loaded, /Παιδί Βήτα/);
  assert.match(loaded, /ΑΚΑΔΗΜΙΑ/);
  assert.match(loaded, /Under Developments/);
  assert.doesNotMatch(loaded, /phone|birthday|medical_notes|guardian_email/);
  const empty = render(MobileFamily,{props:{...props,family:{children:[]},status:'ready'}}).body;
  assert.match(empty, /data-testid="family-empty"/);
  assert.doesNotMatch(empty, /Παιδί Άλφα/);
  const unavailable = render(MobileFamily,{props:{...props,family:null,status:'error',error:'Προσωρινά μη διαθέσιμο'}}).body;
  assert.match(unavailable, /data-testid="family-unavailable"/);
  assert.doesNotMatch(unavailable, /Παιδί Άλφα/);
});
