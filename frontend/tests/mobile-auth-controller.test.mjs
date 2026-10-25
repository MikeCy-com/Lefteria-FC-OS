import test from 'node:test';
import assert from 'node:assert/strict';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';

function identity(role = 'parent', patch = {}) {
  return {
    id: '11111111-1111-4111-8111-111111111111', phone: '+35799123456', role,
    name: 'Test identity', platform: 'Web/PWA', created_at: '2026-01-01T00:00:00Z',
    last_login: '2026-10-25T00:00:00Z', available_roles: [role, 'fan'], current_role_available: true,
    ...patch
  };
}

function setup(handler, initial = {}) {
  const values = new Map(Object.entries(initial));
  const writes = [];
  const storage = createMobileSessionStorage({
    getItem: (key) => values.get(key) ?? null,
    setItem(key, value) { values.set(key, value); writes.push([key, value]); },
    removeItem: (key) => values.delete(key)
  });
  const requests = [];
  const controller = createMobileAuthController({ baseUrl: 'https://club.example', storage, now: () => 1000,
    fetch: async (request) => {
      const body = ['POST', 'PUT'].includes(request.method) ? await request.clone().json() : undefined;
      requests.push({ path: new URL(request.url).pathname, method: request.method, body,
        authorization: request.headers.get('Authorization'), cache: request.cache });
      return handler(requests.at(-1), requests);
    }
  });
  return { controller, requests, values, writes };
}

test('bootstrap validates token and replaces cached roles with current server grants', async () => {
  const { controller, requests, values } = setup(() => Response.json(identity('parent', { available_roles: ['parent', 'fan'] })),
    { mobile_token: 'existing-token', mobile_available_roles: '["management"]' });
  await controller.bootstrap();
  assert.equal(controller.getState().status, 'authenticated');
  assert.equal(controller.getState().user.role, 'parent');
  assert.deepEqual(controller.getState().availableRoles, ['parent', 'fan']);
  assert.equal(values.get('mobile_available_roles'), '["parent","fan"]');
  assert.deepEqual(requests.map(({ path }) => path), ['/api/mobile/auth/me']);
  assert.equal(requests[0].authorization, 'Bearer existing-token');
  assert.equal(requests[0].cache, 'no-store');
});

test('expired bootstrap session removes both legacy storage keys', async () => {
  const { controller, values } = setup(() => Response.json({ detail: 'Token expired', code: 'session_expired' }, { status: 401 }),
    { mobile_token: 'expired-token', mobile_available_roles: '["fan"]' });
  await controller.bootstrap();
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(controller.getState().error, 'Token expired');
  assert.equal(values.has('mobile_token'), false);
  assert.equal(values.has('mobile_available_roles'), false);
});

test('network failure retains a saved token for explicit retry without exposing cached identity', async () => {
  const { controller, values } = setup(() => { throw new TypeError('Network unavailable'); }, { mobile_token: 'existing-token' });
  await controller.bootstrap();
  assert.equal(controller.getState().status, 'unavailable');
  assert.equal(controller.getState().user, null);
  assert.equal(values.get('mobile_token'), 'existing-token');
});

test('OTP remains transient through explicit role selection and sends chosen remember value', async () => {
  const { controller, requests, writes, values } = setup(({ path, body }) => {
    if (path.endsWith('/request-otp')) return Response.json({ phone: '+35799123456', role_detected: null, expires_in: 300, message: 'sent' });
    if (!body.role) return Response.json({ multi_role: true, phone: '+35799123456', identities: [
      { role: 'parent', name: 'Test parent' }, { role: 'fan', name: 'Test fan' }
    ] });
    return Response.json({ token: 'parent-token', user: identity(), role: 'parent', available_roles: ['parent', 'fan'] });
  });
  await controller.bootstrap();
  await controller.requestOtp('99 123456');
  assert.equal(controller.getState().resendAvailableAt, 61000);
  await controller.verifyOtp('842913', null);
  assert.equal(controller.getState().step, 'choose_role');
  assert.equal(values.has('mobile_token'), false);
  assert.equal(JSON.stringify(controller.getState()).includes('842913'), false);
  assert.equal(await controller.chooseRole('parent', 60), true);
  assert.deepEqual(requests.at(-1).body, { phone: '+35799123456', code: '842913', remember_days: 60, role: 'parent' });
  assert.equal(values.get('mobile_token'), 'parent-token');
  assert.ok(writes.every(([key]) => key === 'mobile_token' || key === 'mobile_available_roles'));
  assert.equal(writes.some(([, value]) => value.includes('842913')), false);
});

test('remember selection is always explicit and accepts only null, 30, 60 or 90', async () => {
  for (const remember of [null, 30, 60, 90]) {
    const { controller, requests } = setup(() => Response.json({ token: 'fan-token', user: identity('fan'), role: 'fan', available_roles: ['fan'] }));
    await controller.bootstrap();
    await controller.verifyOtp('123456', remember);
    assert.equal(Object.hasOwn(requests[0].body, 'remember_days'), true);
    assert.equal(requests[0].body.remember_days, remember);
  }
  for (const remember of [undefined, 0, 29, '30', 30.5, true]) {
    const { controller, requests } = setup(() => { throw new Error('must not request'); });
    await controller.bootstrap();
    assert.equal(await controller.verifyOtp('123456', remember), false);
    assert.equal(requests.length, 0);
  }
});

test('fan registration sends source field names and waits for a real OTP', async () => {
  const { controller, requests, values } = setup(() => Response.json({ phone: '+35799123456', role_detected: 'fan', simulated: false }));
  await controller.bootstrap();
  await controller.registerFan({ firstName: 'Test', lastName: 'Fan', phone: '+35799123456', birthday: '2000-01-01' });
  assert.deepEqual(requests[0].body, { first_name: 'Test', last_name: 'Fan', name: null, phone: '+35799123456', birthday: '2000-01-01' });
  assert.equal(controller.getState().step, 'otp');
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(values.has('mobile_token'), false);
});

test('provider failure displays API detail and does not advance to OTP', async () => {
  const { controller, requests } = setup(() => Response.json({ detail: 'SMS service unavailable', code: 'delivery_unavailable' }, { status: 503 }));
  await controller.bootstrap();
  assert.equal(await controller.requestOtp('+35799123456'), false);
  assert.equal(controller.getState().step, 'phone');
  assert.equal(controller.getState().error, 'SMS service unavailable');
  assert.equal(controller.getState().busy, false);
  assert.equal(requests.length, 1);
});

test('role no-op keeps its token and refreshes current server grants', async () => {
  const { controller, values, requests } = setup(({ path }) => path.endsWith('/switch-role')
    ? Response.json({ token: null, role: 'parent', noop: true }) : Response.json(identity()), { mobile_token: 'same-token' });
  await controller.bootstrap();
  assert.equal(await controller.switchRole('parent'), true);
  assert.equal(values.get('mobile_token'), 'same-token');
  assert.deepEqual(requests.map(({ path }) => path), ['/api/mobile/auth/me', '/api/mobile/auth/switch-role', '/api/mobile/auth/me']);
});

test('revoked role stays selected until explicit fan recovery and does not permit other role switches', async () => {
  const { controller, requests } = setup(({ path }) => path.endsWith('/me')
    ? Response.json(identity('management', { current_role_available: false, available_roles: ['fan', 'coach'] }))
    : Response.json({ token: 'fan-token', role: 'fan', user: identity('fan', { available_roles: ['fan', 'coach'] }), available_roles: ['fan', 'coach'] }),
  { mobile_token: 'revoked-role-token' });
  await controller.bootstrap();
  assert.equal(controller.getState().user.role, 'management');
  assert.equal(controller.getState().user.current_role_available, false);
  assert.equal(requests.length, 1);
  assert.equal(await controller.switchRole('coach'), false);
  assert.equal(requests.length, 1);
  assert.equal(await controller.switchRole('fan'), true);
  assert.equal(controller.getState().user.role, 'fan');
});

test('management organization selection uses the authorized token and clears context on role change', async () => {
  const organizations = [{ id: 'club', name: 'Club' }, { id: 'academy', name: 'Academy' }];
  const { controller, requests } = setup(({ path, method, body }) => {
    if (path.endsWith('/me')) return Response.json(identity('management'));
    if (path.endsWith('/switch-role')) return Response.json({ token: 'fan-token', user: identity('fan'), role: 'fan', available_roles: ['fan', 'management'] });
    return Response.json({ available_organizations: organizations, organization_id: method === 'POST' ? body.organization_id : null,
      organization_name: method === 'POST' ? 'Academy' : null, requires_organization_selection: method !== 'POST' });
  }, { mobile_token: 'management-token' });
  await controller.bootstrap();
  assert.equal(controller.getState().organization.requires_organization_selection, true);
  assert.equal(controller.getState().organization.organization_id, null);
  await controller.selectOrganization('academy');
  assert.equal(controller.getState().organization.organization_id, 'academy');
  assert.equal(requests.at(-1).authorization, 'Bearer management-token');
  assert.deepEqual(requests.at(-1).body, { organization_id: 'academy' });
  await controller.switchRole('fan');
  assert.equal(controller.getState().organization, null);
  assert.equal(controller.getState().user.role, 'fan');
});

test('logout defeats a late OTP response and clears transient verification proof', async () => {
  let complete;
  const { controller, values } = setup(() => new Promise((resolve) => { complete = resolve; }));
  await controller.bootstrap();
  const request = controller.requestOtp('+35799123456');
  await new Promise((resolve) => setImmediate(resolve));
  controller.logout();
  complete(Response.json({ phone: '+35799123456', role_detected: 'fan', expires_in: 300, message: 'sent' }));
  assert.equal(await request, false);
  assert.equal(controller.getState().step, 'phone');
  assert.equal(controller.getState().busy, false);
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(values.has('mobile_token'), false);
});

test('storage write failure clears an older saved session rather than restoring the wrong role', () => {
  const values = new Map([['mobile_token', 'older-token'], ['mobile_available_roles', '["management"]']]);
  const storage = createMobileSessionStorage({
    getItem: (key) => values.get(key) ?? null,
    setItem() { throw new Error('Storage quota exceeded'); },
    removeItem: (key) => values.delete(key)
  });
  storage.write('newer-fan-token', ['fan']);
  assert.equal(storage.readToken(), null);
  assert.equal(values.size, 0);
});

test('failed organization or management role change keeps stale context cleared and permits explicit recovery', async () => {
  for (const operation of ['organization', 'role']) {
    let currentOrganizationReads = 0;
    const { controller } = setup(({ path, method }) => {
      if (path.endsWith('/me')) return Response.json(identity('management'));
      if (method === 'POST') return Response.json({ detail: 'Assignment changed', code: 'organization_unknown' }, { status: 403 });
      currentOrganizationReads++;
      return Response.json({ available_organizations: [{ id: 'club', name: 'Club' }],
        organization_id: 'club', organization_name: 'Club', requires_organization_selection: false });
    }, { mobile_token: 'management-token' });
    await controller.bootstrap();
    assert.equal(controller.getState().organization.organization_id, 'club');
    const success = operation === 'organization'
      ? await controller.selectOrganization('academy') : await controller.switchRole('coach');
    assert.equal(success, false);
    assert.equal(controller.getState().organization, null);
    assert.equal(controller.getState().organizationError, 'Assignment changed');
    assert.equal(controller.getState().user.role, 'management');
    assert.equal(await controller.refreshOrganization(), true);
    assert.equal(currentOrganizationReads, 2);
    assert.equal(controller.getState().organization.organization_id, 'club');
    assert.equal(controller.getState().organizationError, null);
    assert.equal(controller.getState().error, null);
  }
});

test('team preference PUT uses each selected role identity JWT with no caller-selected target or role switch', async () => {
  for (const role of ['fan', 'parent', 'player', 'coach', 'management']) {
    const profile = identity(role, { favorite_team: 'first_team' });
    const { controller, requests, values } = setup(({ path, method, body }) => {
      if (path.endsWith('/organization')) return Response.json({ available_organizations: [{ id: 'club', name: 'Club' }],
        organization_id: 'club', organization_name: 'Club', requires_organization_selection: false });
      if (method === 'GET') return Response.json(profile);
      return Response.json({ ok: true, user: { ...profile, favorite_team: body.favorite_team } });
    }, { mobile_token: `${role}-token` });
    await controller.bootstrap();
    assert.equal(await controller.updateFavoriteTeam('academy'), true);
    assert.equal(requests.at(-1).method, 'PUT');
    assert.equal(requests.at(-1).path, '/api/mobile/auth/me');
    assert.equal(requests.at(-1).authorization, `Bearer ${role}-token`);
    assert.equal(requests.at(-1).cache, 'no-store');
    assert.deepEqual(requests.at(-1).body, { favorite_team: 'academy' });
    assert.equal(controller.getState().user.id, profile.id);
    assert.equal(controller.getState().user.role, role);
    assert.equal(controller.getState().user.favorite_team, 'academy');
    assert.equal(values.get('mobile_token'), `${role}-token`);
    assert.equal(requests.some(({ path }) => path.endsWith('/switch-role')), false);
    if (role === 'management') assert.equal(controller.getState().organization.organization_id, 'club');
  }
});

test('preference refresh preserves independent parent and fan values through explicit role switches', async () => {
  const profiles = {
    parent: identity('parent', { favorite_team: 'academy' }),
    fan: identity('fan', { id: '22222222-2222-4222-8222-222222222222', favorite_team: 'both', available_roles: ['parent', 'fan'] })
  };
  const { controller, requests } = setup(({ path, method, body, authorization }) => {
    if (path.endsWith('/switch-role')) return Response.json({ token: `${body.role}-token`, role: body.role,
      user: profiles[body.role], available_roles: ['parent', 'fan'] });
    const role = authorization === 'Bearer parent-token' ? 'parent' : 'fan';
    if (method === 'PUT') {
      profiles[role] = { ...profiles[role], favorite_team: body.favorite_team };
      return Response.json({ ok: true, user: profiles[role] });
    }
    return Response.json(profiles[role]);
  }, { mobile_token: 'parent-token' });
  await controller.bootstrap();
  await controller.updateFavoriteTeam('first_team');
  await controller.switchRole('fan');
  assert.equal(controller.getState().user.favorite_team, 'both');
  await controller.updateFavoriteTeam('academy');
  await controller.switchRole('parent');
  assert.equal(controller.getState().user.favorite_team, 'first_team');
  assert.equal(profiles.fan.favorite_team, 'academy');
  assert.deepEqual(requests.filter(({ method }) => method === 'PUT').map(({ authorization }) => authorization),
    ['Bearer parent-token', 'Bearer fan-token']);
});

test('preference selection explicitly supports first team, academy, both and null clearing', async () => {
  for (const selection of ['first_team', 'academy', 'both', null]) {
    const profile = identity('fan', { favorite_team: 'academy' });
    const { controller, requests } = setup(({ method }) => method === 'PUT'
      ? Response.json({ ok: true, user: { ...profile, favorite_team: selection } }) : Response.json(profile),
    { mobile_token: 'fan-token' });
    await controller.bootstrap();
    assert.equal(await controller.updateFavoriteTeam(selection), true);
    assert.deepEqual(requests.at(-1).body, { favorite_team: selection });
    assert.equal(controller.getState().user.favorite_team, selection);
  }
});

test('failed preference updates retain actual profile and token without optimistic changes', async () => {
  for (const failure of [422, 503, 'network']) {
    const profile = identity('fan', { favorite_team: 'first_team' });
    const { controller, values } = setup(({ method }) => {
      if (method === 'GET') return Response.json(profile);
      if (failure === 'network') throw new TypeError('Network unavailable');
      return Response.json({ detail: 'Preference could not be saved', code: 'invalid_team_preference' }, { status: failure });
    }, { mobile_token: 'fan-token' });
    await controller.bootstrap();
    assert.equal(await controller.updateFavoriteTeam('academy'), false);
    assert.deepEqual(controller.getState().user, profile);
    assert.equal(controller.getState().status, 'authenticated');
    assert.equal(controller.getState().busy, false);
    assert.equal(values.get('mobile_token'), 'fan-token');
    assert.ok(controller.getState().error);
    if (failure !== 'network') assert.equal(controller.getState().error, 'Preference could not be saved');
  }
});

test('expired preference request clears the session rather than retaining a private profile', async () => {
  const { controller, values } = setup(({ method }) => method === 'GET' ? Response.json(identity('fan'))
    : Response.json({ detail: 'Token expired', code: 'session_expired' }, { status: 401 }), { mobile_token: 'expired-token' });
  await controller.bootstrap();
  assert.equal(await controller.updateFavoriteTeam('both'), false);
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(controller.getState().user, null);
  assert.equal(values.has('mobile_token'), false);
});

test('logout or a different restored session defeats an old preference response', async () => {
  for (const restoreAnotherSession of [false, true]) {
    let complete;
    const oldProfile = identity('fan', { favorite_team: 'first_team' });
    const newProfile = identity('parent', { id: '22222222-2222-4222-8222-222222222222', favorite_team: 'both' });
    const { controller, values } = setup(({ method, authorization }) => method === 'PUT'
      ? new Promise((resolve) => { complete = resolve; })
      : Response.json(authorization === 'Bearer parent-token' ? newProfile : oldProfile), { mobile_token: 'fan-token' });
    await controller.bootstrap();
    const request = controller.updateFavoriteTeam('academy');
    await new Promise((resolve) => setImmediate(resolve));
    assert.equal(controller.getState().busy, true);
    controller.logout();
    if (restoreAnotherSession) {
      values.set('mobile_token', 'parent-token');
      await controller.bootstrap();
    }
    complete(Response.json({ ok: true, user: { ...oldProfile, favorite_team: 'academy' } }));
    assert.equal(await request, false);
    assert.equal(controller.getState().busy, false);
    if (restoreAnotherSession) {
      assert.deepEqual(controller.getState().user, newProfile);
      assert.equal(values.get('mobile_token'), 'parent-token');
    } else {
      assert.equal(controller.getState().status, 'anonymous');
      assert.equal(controller.getState().user, null);
      assert.equal(values.has('mobile_token'), false);
    }
  }
});

test('preference response cannot replace the selected identity or role', async () => {
  for (const mismatch of [{ role: 'parent' }, { id: '22222222-2222-4222-8222-222222222222' }, { phone: '+35799111111' }]) {
    const profile = identity('fan', { favorite_team: 'first_team' });
    const { controller, values } = setup(({ method }) => method === 'GET' ? Response.json(profile)
      : Response.json({ ok: true, user: { ...profile, ...mismatch, favorite_team: 'academy' } }), { mobile_token: 'fan-token' });
    await controller.bootstrap();
    assert.equal(await controller.updateFavoriteTeam('academy'), false);
    assert.deepEqual(controller.getState().user, profile);
    assert.equal(values.get('mobile_token'), 'fan-token');
  }
});

test('preference updates deny anonymous and revoked identities without sending a write', async () => {
  const anonymous = setup(() => { throw new Error('No request expected'); });
  await anonymous.controller.bootstrap();
  assert.equal(await anonymous.controller.updateFavoriteTeam('both'), false);
  assert.equal(anonymous.requests.length, 0);
  const revoked = setup(() => Response.json(identity('fan', { current_role_available: false })), { mobile_token: 'revoked-token' });
  await revoked.controller.bootstrap();
  assert.equal(await revoked.controller.updateFavoriteTeam('both'), false);
  assert.equal(revoked.requests.length, 1);
});
