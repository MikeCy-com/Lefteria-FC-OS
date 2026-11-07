import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';

const uuid = '11111111-1111-4111-8111-111111111111';
const inviteId = '22222222-2222-4222-8222-222222222222';
const parent = {
  id: uuid, phone: '+35799123456', role: 'parent',
  name: 'Συνθετικός Γονέας', platform: 'Web/PWA',
  created_at: '2026-01-01T00:00:00Z', last_login: '2026-11-07T00:00:00Z',
  current_role_available: true, available_roles: ['parent', 'fan']
};
const family = {
  children: [
    { id: 'child-a', name: 'Παιδί Άλφα', organization_id: 'lefteria_fc_academy',
      team_type: 'Academy', team_id: null, academy_group_ids: ['u12'] },
    { id: 'child-b', name: 'Παιδί Βήτα', organization_id: 'lefteria_fc',
      team_type: 'First Team', team_id: 'club-team', academy_group_ids: [] }
  ]
};
function outgoing(overrides = {}) {
  return { items: [{
    id: inviteId, player_id: 'child-a', organization_id: 'lefteria_fc_academy',
    status: 'pending', created_at: new Date(Date.now() - 30_000).toISOString(),
    expires_at: new Date(Date.now() + 86_400_000).toISOString(), ...overrides
  }] };
}
function setup(handler, role = 'parent') {
  const storage = createMobileSessionStorage({
    getItem: key => key === 'mobile_token' ? 'synthetic-token' : null,
    setItem: () => {}, removeItem: () => {}
  });
  const requests = [];
  const controller = createMobileAuthController({
    baseUrl: 'https://club.example', storage,
    fetch: async request => {
      const row = { path: new URL(request.url).pathname,
        authorization: request.headers.get('Authorization'), method: request.method };
      requests.push(row);
      return await handler(row);
    }
  });
  return { controller, requests };
}

test('parent reads current outgoing reservations only after verified family; no contact data is persisted', async () => {
  const { controller, requests } = setup(({path}) => {
    if (path === '/api/mobile/auth/me') return Response.json(parent);
    if (path === '/api/mobile/family') return Response.json(family);
    if (path === '/api/mobile/family/outgoing-invitations') return Response.json(outgoing());
    throw Error('Unexpected path ' + path);
  });
  await controller.bootstrap();
  assert.equal(await controller.loadOutgoingInvitations(), false);
  assert.equal(requests.filter(r => r.path.includes('outgoing')).length, 0);
  assert.equal(await controller.loadFamily(), true);
  assert.equal(await controller.loadOutgoingInvitations(), true);
  assert.equal(controller.getState().outgoingStatus, 'ready');
  assert.equal(controller.getState().outgoingInvitations.items.length, 1);
  assert.deepEqual(requests.at(-1), {
    path: '/api/mobile/family/outgoing-invitations', method: 'GET',
    authorization: 'Bearer synthetic-token'
  });
  const stored = JSON.stringify(controller.getState().outgoingInvitations);
  assert.doesNotMatch(stored, /35799123456|target_phone|contact|otp/);
  controller.logout();
  assert.equal(controller.getState().outgoingInvitations, null);
  assert.equal(controller.getState().outgoingStatus, 'idle');
});

test('outgoing records from unrelated children, another organization, duplicate slots or malformed dates are rejected', async () => {
  for (const invalid of [
    outgoing({ player_id: 'other-child' }),
    outgoing({ organization_id: 'lefteria_fc' }),
    outgoing({ status: 'active' }),
    outgoing({ expires_at: 'not-a-date' }),
    { items: [outgoing().items[0], outgoing().items[0]] },
    { items: null }
  ]) {
    const { controller } = setup(({path}) => path === '/api/mobile/auth/me'
      ? Response.json(parent) : path === '/api/mobile/family'
      ? Response.json(family) : Response.json(invalid));
    await controller.bootstrap();
    assert.equal(await controller.loadFamily(), true);
    assert.equal(await controller.loadOutgoingInvitations(), false);
    assert.equal(controller.getState().outgoingInvitations, null);
    assert.equal(controller.getState().outgoingStatus, 'error');
  }
});

test('nonparent cannot call outgoing route, and family revalidation erases prior invitation list', async () => {
  const wrong = setup(({path}) => {
    if (path === '/api/mobile/auth/me') return Response.json({ ...parent,
      role: 'coach', available_roles: ['coach', 'fan'] });
    if (path === '/api/mobile/family') return Response.json(family);
    throw Error('Forbidden outgoing request');
  });
  await wrong.controller.bootstrap();
  assert.equal(await wrong.controller.loadOutgoingInvitations(), false);
  assert.equal(wrong.requests.length, 1);

  let revoked = false;
  const { controller } = setup(({path}) => path === '/api/mobile/auth/me'
    ? Response.json(parent) : path === '/api/mobile/family'
    ? revoked ? Response.json({children:[]}) : Response.json(family)
    : Response.json(outgoing()));
  await controller.bootstrap();
  assert.equal(await controller.loadFamily(), true);
  assert.equal(await controller.loadOutgoingInvitations(), true);
  revoked = true;
  assert.equal(await controller.loadFamily(), true);
  assert.equal(controller.getState().outgoingInvitations, null);
  assert.equal(controller.getState().outgoingStatus, 'idle');
  assert.equal(await controller.loadOutgoingInvitations(), false);
  assert.equal(controller.getState().outgoingInvitations, null);
  assert.equal(controller.getState().outgoingStatus, 'error');
});

test('stale outgoing HTTP response after logout cannot restore private details', async () => {
  const delayed = Promise.withResolvers();
  const { controller } = setup(({path}) => path === '/api/mobile/auth/me'
    ? Response.json(parent) : path === '/api/mobile/family'
    ? Response.json(family) : delayed.promise);
  await controller.bootstrap();
  assert.equal(await controller.loadFamily(), true);
  const loading = controller.loadOutgoingInvitations();
  assert.equal(controller.getState().outgoingStatus, 'loading');
  controller.logout();
  delayed.resolve(Response.json(outgoing()));
  assert.equal(await loading, false);
  assert.equal(controller.getState().outgoingInvitations, null);
});

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.endsWith('.css'))
      return { url: 'data:text/javascript,export%20default%20%7B%7D', shortCircuit: true };
    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (url.endsWith('.svelte')) {
      const filename = fileURLToPath(url);
      return { format: 'module', source: compile(readFileSync(filename,'utf8'),
        {filename, generate:'server'}).js.code, shortCircuit:true };
    }
    return nextLoad(url,context);
  }
});

test('real Svelte parent Children screen labels pending as NOT sent and never prints a target phone', async () => {
  const {default: MobileFamily} = await import('../src/lib/mobile/components/MobileFamily.svelte');
  const props = { family, status:'ready', error:null, busy:false,
    onLoad:()=>true, onCalendar:()=>{}, onLoadOutgoing:()=>true,
    outgoingInvitations:outgoing(), outgoingStatus:'ready', outgoingError:null };
  const html = render(MobileFamily,{props}).body;
  assert.match(html, /data-testid="family-outgoing-invitations"/);
  assert.match(html, /data-testid="family-outgoing-list"/);
  assert.match(html, /Παιδί Άλφα/);
  assert.match(html, /Δεν έχει αποσταλεί πρόσκληση ή SMS/);
  assert.match(html, /Under Developments/);
  assert.doesNotMatch(html,/target_phone|recipient_phone|\\+35799123456|Παιδί Γάμμα/);

  const empty = render(MobileFamily,{props:{...props,
    outgoingInvitations:{items:[]}}}).body;
  assert.match(empty,/data-testid="family-outgoing-empty"/);
  const unavailable = render(MobileFamily,{props:{...props,outgoingInvitations:null,
    outgoingStatus:'error', outgoingError:'Προσωρινή βλάβη'}}).body;
  assert.match(unavailable,/data-testid="family-outgoing-error"/);
  assert.doesNotMatch(unavailable,/data-testid="family-outgoing-row"/);

  const route = readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  assert.match(route,/outgoingInvitations=\{\$controller\.outgoingInvitations\}/);
  assert.match(route,/onLoadOutgoing=\{\(\) => controller\.loadOutgoingInvitations\(\)\}/);
  assert.match(route,/activeTab === 'children' && role === 'parent' && !composing/);
});
