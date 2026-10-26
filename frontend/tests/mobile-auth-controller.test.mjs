import test from 'node:test';
import assert from 'node:assert/strict';
import { avatarFileError, createMobileAuthController, MAX_AVATAR_FILE_BYTES } from '../src/lib/mobile/controller.ts';
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
      const contentType = request.headers.get('Content-Type') ?? '';
      const body = !['POST', 'PUT'].includes(request.method) ? undefined
        : contentType.startsWith('multipart/form-data') ? await request.clone().formData() : await request.clone().json();
      requests.push({ path: new URL(request.url).pathname, search: new URL(request.url).search, method: request.method, body,
        authorization: request.headers.get('Authorization'), cache: request.cache, contentType });
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

test('own profile details PUT applies fresh server fields and timestamp without changing JWT or role', async () => {
  for (const role of ['fan', 'parent', 'player', 'coach', 'management']) {
    const profile = identity(role, { first_name: 'Old', last_name: 'Name', email: 'old@example.test' });
    const changed = { ...profile, first_name: 'Updated', last_name: 'Member', name: 'Updated Member',
      birthday: '2000-01-02', email: '', updated_at: '2026-10-25T18:30:00Z' };
    const { controller, requests, values } = setup(({ path, method }) => {
      if (path.endsWith('/organization')) return Response.json({ available_organizations: [{ id: 'club', name: 'Club' }],
        organization_id: 'club', organization_name: 'Club', requires_organization_selection: false });
      return Response.json(method === 'PUT' ? changed : profile);
    }, { mobile_token: `${role}-token` });
    await controller.bootstrap();
    assert.equal(await controller.updateProfile({ first_name: 'Updated', last_name: 'Member', birthday: '2000-01-02', email: '' }), true);
    assert.deepEqual(controller.getState().user, changed);
    assert.equal(requests.at(-1).path, '/api/mobile/profile');
    assert.equal(requests.at(-1).authorization, `Bearer ${role}-token`);
    assert.deepEqual(requests.at(-1).body, { first_name: 'Updated', last_name: 'Member', birthday: '2000-01-02', email: '' });
    assert.equal(values.get('mobile_token'), `${role}-token`);
    if (role === 'management') assert.equal(controller.getState().organization.organization_id, 'club');
  }
});

test('profile updates whitelist editable fields, ignore nulls and retain existing boolean preference keys', async () => {
  const profile = identity('fan', { notification_prefs: { news: true, legacy_opt_in: false }, email: 'old@example.test' });
  const preferences = { ...profile.notification_prefs, news: false, game_hub_giveaways: true };
  const { controller, requests } = setup(({ method, body }) => Response.json(method === 'PUT'
    ? { ...profile, ...body, updated_at: '2026-10-25T18:31:00Z' } : profile), { mobile_token: 'fan-token' });
  await controller.bootstrap();
  assert.equal(await controller.updateProfile({ name: null, email: '', first_name: null, birthday: null,
    notification_prefs: preferences, phone: '+35799111111', role: 'management', id: 'other-target', avatar_url: 'unapproved.jpg' }), true);
  assert.deepEqual(requests.at(-1).body, { email: '', notification_prefs: preferences });
  assert.equal(controller.getState().user.phone, profile.phone);
  assert.equal(controller.getState().user.notification_prefs.legacy_opt_in, false);
  assert.equal(controller.getState().user.notification_prefs.news, false);
});

test('rejected profile update retains saved details and preferences', async () => {
  for (const status of [400, 422, 503]) {
    const profile = identity('fan', { first_name: 'Saved', last_name: 'Member', notification_prefs: { news: true } });
    const { controller } = setup(({ method }) => method === 'PUT'
      ? Response.json({ detail: 'Profile could not be updated', code: 'invalid_profile' }, { status }) : Response.json(profile),
    { mobile_token: 'fan-token' });
    await controller.bootstrap();
    assert.equal(await controller.updateProfile({ first_name: 'Unsaved', notification_prefs: { news: false } }), false);
    assert.deepEqual(controller.getState().user, profile);
    assert.equal(controller.getState().error, 'Profile could not be updated');
    assert.equal(controller.getState().busy, false);
  }
});

test('old profile response cannot restore private fields after logout or replace a newer session', async () => {
  for (const restoreAnotherSession of [false, true]) {
    let complete;
    const profile = identity('fan', { name: 'Previous identity', email: 'previous@example.test' });
    const restored = identity('parent', { id: '22222222-2222-4222-8222-222222222222', name: 'Current identity' });
    const { controller, values } = setup(({ method, authorization }) => method === 'PUT'
      ? new Promise((resolve) => { complete = resolve; })
      : Response.json(authorization === 'Bearer parent-token' ? restored : profile), { mobile_token: 'fan-token' });
    await controller.bootstrap();
    const request = controller.updateProfile({ email: 'updated@example.test' });
    await new Promise((resolve) => setImmediate(resolve));
    assert.deepEqual(controller.getState().user, profile);
    controller.logout();
    if (restoreAnotherSession) {
      values.set('mobile_token', 'parent-token');
      await controller.bootstrap();
    }
    complete(Response.json({ ...profile, email: 'updated@example.test' }));
    assert.equal(await request, false);
    assert.equal(controller.getState().busy, false);
    assert.deepEqual(controller.getState().user, restoreAnotherSession ? restored : null);
  }
});

test('profile endpoint response cannot change own identity binding and unauthenticated writes are denied', async () => {
  const profile = identity('fan');
  const { controller, requests } = setup(({ method }) => Response.json(method === 'GET' ? profile : { ...profile, role: 'management' }),
    { mobile_token: 'fan-token' });
  await controller.bootstrap();
  assert.equal(await controller.updateProfile({ name: 'Updated name' }), false);
  assert.deepEqual(controller.getState().user, profile);
  controller.logout();
  assert.equal(await controller.updateProfile({ name: 'Updated name' }), false);
  assert.equal(requests.length, 2);
});

const avatarBytes = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jCfcAAAAASUVORK5CYII=', 'base64');
const avatarFile = () => new File([avatarBytes], 'profile portrait.png', { type: 'image/png' });

test('avatar upload sends one authenticated multipart file and applies only the refreshed same-role profile', async () => {
  for (const role of ['fan', 'parent', 'player', 'coach', 'management']) {
    const profile = identity(role, { avatar_url: '/api/uploads/avatars/previous.jpg' });
    let stored = profile;
    const avatarUrl = '/api/uploads/avatars/verified-photo.jpg';
    const { controller, requests, values } = setup(({ path, method }) => {
      if (path.endsWith('/organization')) return Response.json({ available_organizations: [{ id: 'club', name: 'Club' }],
        organization_id: 'club', organization_name: 'Club', requires_organization_selection: false });
      if (method === 'POST') {
        assert.equal(controller.getState().user.avatar_url, profile.avatar_url);
        stored = { ...profile, avatar_url: avatarUrl, updated_at: '2026-10-26T01:00:00Z' };
        return Response.json({ avatar_url: avatarUrl });
      }
      if (stored !== profile) assert.equal(controller.getState().user.avatar_url, profile.avatar_url);
      return Response.json(stored);
    }, { mobile_token: `${role}-token` });
    await controller.bootstrap();
    assert.equal(await controller.uploadAvatar(avatarFile()), true);
    const upload = requests.find(({ method }) => method === 'POST');
    assert.equal(upload.path, '/api/mobile/profile/avatar');
    assert.equal(upload.authorization, `Bearer ${role}-token`);
    assert.match(upload.contentType, /^multipart\/form-data; boundary=.+/);
    assert.deepEqual([...upload.body.keys()], ['file']);
    const file = upload.body.get('file');
    assert.equal(file.name, 'profile portrait.png');
    assert.equal(file.type, 'image/png');
    assert.deepEqual(Buffer.from(await file.arrayBuffer()), avatarBytes);
    assert.equal(requests.at(-1).method, 'GET');
    assert.equal(requests.at(-1).path, '/api/mobile/auth/me');
    assert.equal(requests.at(-1).authorization, `Bearer ${role}-token`);
    assert.deepEqual(controller.getState().user, stored);
    assert.equal(values.get('mobile_token'), `${role}-token`);
    assert.equal(requests.some(({ path }) => path.endsWith('/switch-role')), false);
    if (role === 'management') assert.equal(controller.getState().organization.organization_id, 'club');
  }
});

test('avatar client prechecks reject unsupported, empty and oversized files before HTTP', async () => {
  const { controller, requests } = setup(() => Response.json(identity('fan')), { mobile_token: 'fan-token' });
  await controller.bootstrap();
  for (const file of [
    new File(['<svg/>'], 'avatar.svg', { type: 'image/svg+xml' }),
    new File(['text'], 'avatar.jpg', { type: 'text/html' }),
    new File([], 'empty.png', { type: 'image/png' }),
    new File([new Uint8Array(MAX_AVATAR_FILE_BYTES + 1)], 'large.png', { type: 'image/png' })
  ]) {
    assert.ok(avatarFileError(file));
    assert.equal(await controller.uploadAvatar(file), false);
    assert.equal(controller.getState().busy, false);
  }
  assert.equal(requests.length, 1);
  for (const type of ['image/jpeg', 'image/png', 'image/webp', 'image/gif']) {
    assert.equal(avatarFileError(new File([avatarBytes], 'avatar', { type })), null);
  }
  assert.equal(avatarFileError(new File([new Uint8Array(MAX_AVATAR_FILE_BYTES)], 'limit.jpg', { type: 'image/jpeg' })), null);
});

test('avatar server rejections retain the saved profile and never trigger a speculative refresh', async () => {
  for (const status of [400, 413, 415, 422, 503]) {
    const profile = identity('fan', { avatar_url: '/api/uploads/avatars/saved.jpg' });
    const { controller, requests, values } = setup(({ method }) => method === 'POST'
      ? Response.json({ detail: 'Photo could not be saved', code: 'invalid_avatar' }, { status }) : Response.json(profile),
    { mobile_token: 'fan-token' });
    await controller.bootstrap();
    assert.equal(await controller.uploadAvatar(avatarFile()), false);
    assert.deepEqual(controller.getState().user, profile);
    assert.equal(controller.getState().error, 'Photo could not be saved');
    assert.equal(requests.length, 2);
    assert.equal(values.get('mobile_token'), 'fan-token');
  }
});

test('successful avatar POST does not change the profile if confirming GET fails', async () => {
  for (const failure of ['network', 503, 401]) {
    const profile = identity('fan', { avatar_url: '/api/uploads/avatars/saved.jpg' });
    let posted = false;
    const { controller, values } = setup(({ method }) => {
      if (method === 'POST') { posted = true; return Response.json({ avatar_url: '/api/uploads/avatars/new.jpg' }); }
      if (!posted) return Response.json(profile);
      if (failure === 'network') throw new TypeError('Network unavailable');
      return Response.json({ detail: 'Profile could not be refreshed', code: 'refresh_failed' }, { status: failure });
    }, { mobile_token: 'fan-token' });
    await controller.bootstrap();
    assert.equal(await controller.uploadAvatar(avatarFile()), false);
    assert.equal(controller.getState().busy, false);
    if (failure === 401) {
      assert.equal(controller.getState().status, 'anonymous');
      assert.equal(controller.getState().user, null);
      assert.equal(values.has('mobile_token'), false);
    } else {
      assert.deepEqual(controller.getState().user, profile);
      assert.equal(values.get('mobile_token'), 'fan-token');
    }
  }
});

test('stale avatar POST or confirming GET cannot replace a newer session after logout', { timeout: 5000 }, async () => {
  for (const stage of ['post', 'me']) {
    let complete;
    let signalPending;
    const pending = new Promise((resolve) => { signalPending = resolve; });
    const holdResponse = () => new Promise((resolve) => { complete = resolve; signalPending(); });
    let posted = false;
    const previous = identity('fan', { avatar_url: '/api/uploads/avatars/previous.jpg' });
    const current = identity('parent', { id: '22222222-2222-4222-8222-222222222222', avatar_url: '/api/uploads/avatars/current.jpg' });
    const { controller, requests, values } = setup(({ method, authorization }) => {
      if (method === 'POST') {
        posted = true;
        return stage === 'post' ? holdResponse()
          : Response.json({ avatar_url: '/api/uploads/avatars/uploaded.jpg' });
      }
      if (authorization === 'Bearer parent-token') return Response.json(current);
      if (posted) return holdResponse();
      return Response.json(previous);
    }, { mobile_token: 'fan-token' });
    await controller.bootstrap();
    const request = controller.uploadAvatar(avatarFile());
    await pending;
    assert.equal(await controller.switchRole('parent'), false, 'Role switching is disabled while upload is in progress.');
    controller.logout();
    values.set('mobile_token', 'parent-token');
    await controller.bootstrap();
    complete(Response.json(stage === 'post' ? { avatar_url: '/api/uploads/avatars/uploaded.jpg' }
      : { ...previous, avatar_url: '/api/uploads/avatars/uploaded.jpg' }));
    assert.equal(await request, false);
    assert.deepEqual(controller.getState().user, current);
    assert.equal(values.get('mobile_token'), 'parent-token');
    assert.equal(requests.filter(({ method }) => method === 'POST').length, 1);
  }
});

test('avatar refresh refuses another identity and anonymous or revoked identities cannot upload', async () => {
  const profile = identity('fan');
  let uploaded = false;
  const { controller, requests } = setup(({ method }) => {
    if (method === 'POST') { uploaded = true; return Response.json({ avatar_url: '/api/uploads/avatars/uploaded.jpg' }); }
    return Response.json(uploaded ? { ...profile, role: 'management' } : profile);
  }, { mobile_token: 'fan-token' });
  await controller.bootstrap();
  assert.equal(await controller.uploadAvatar(avatarFile()), false);
  assert.deepEqual(controller.getState().user, profile);
  controller.logout();
  assert.equal(await controller.uploadAvatar(avatarFile()), false);
  assert.equal(requests.length, 3);
  const revoked = setup(() => Response.json(identity('fan', { current_role_available: false })), { mobile_token: 'revoked-token' });
  await revoked.controller.bootstrap();
  assert.equal(await revoked.controller.uploadAvatar(avatarFile()), false);
  assert.equal(revoked.requests.length, 1);
});

const playerOptions = [
  { id: 'player-option-1', name: 'Directory Player', number: 0, position: 'Goalkeeper', image_url: null },
  { id: 'player-option-2', name: 'Second Player', number: 12, position: 'Defender', image_url: '/api/uploads/roster/second.jpg' }
];

test('player picker loads the authenticated minimal directory for all five roles without changing a stored favourite', async () => {
  for (const role of ['fan', 'parent', 'player', 'coach', 'management']) {
    const profile = identity(role, { favourite_player_id: 'unlisted-saved-id' });
    const { controller, requests, writes, values } = setup(({ path }) => {
      if (path.endsWith('/organization')) return Response.json({ available_organizations: [{ id: 'club', name: 'Club' }],
        organization_id: 'club', organization_name: 'Club', requires_organization_selection: false });
      return Response.json(path.endsWith('/players-list') ? { players: playerOptions } : profile);
    }, { mobile_token: `${role}-token` });
    await controller.bootstrap();
    const priorWrites = writes.length;
    assert.equal(await controller.loadPlayerOptions(), true);
    assert.equal(controller.getState().playerOptionsStatus, 'ready');
    assert.deepEqual(controller.getState().playerOptions, playerOptions);
    assert.equal(controller.getState().playerOptionsError, null);
    assert.deepEqual(controller.getState().user, profile, 'Loading must not clear or replace an unlisted saved reference.');
    assert.equal(writes.length, priorWrites, 'Directory results stay in memory.');
    assert.equal(values.get('mobile_token'), `${role}-token`);
    assert.deepEqual(requests.at(-1), { path: '/api/mobile/players-list', search: '', method: 'GET', body: undefined,
      authorization: `Bearer ${role}-token`, cache: 'no-store', contentType: '' });
    assert.equal(requests.some(({ method }) => method === 'PUT'), false);
    if (role === 'management') assert.equal(controller.getState().organization.organization_id, 'club');
  }
});

test('player picker distinguishes loading, unavailable errors and a verified empty list with explicit retry', { timeout: 5000 }, async () => {
  for (const failure of [503, 403, 'network']) {
    let complete;
    let signal;
    const pending = new Promise((resolve) => { signal = resolve; });
    let attempts = 0;
    const profile = identity('fan', { favourite_player_id: 'saved-id' });
    const { controller } = setup(({ path }) => {
      if (!path.endsWith('/players-list')) return Response.json(profile);
      ++attempts;
      if (attempts === 1) return new Promise((resolve, reject) => { complete = failure === 'network' ? reject : resolve; signal(); });
      return Response.json({ players: [] });
    }, { mobile_token: 'fan-token' });
    await controller.bootstrap();
    const request = controller.loadPlayerOptions();
    await pending;
    assert.equal(controller.getState().playerOptionsStatus, 'loading');
    assert.equal(controller.getState().busy, false, 'An outstanding directory read must not prevent an explicit role switch.');
    assert.equal(await controller.loadPlayerOptions(), false, 'An in-flight load is not duplicated.');
    complete(failure === 'network' ? new TypeError('Network unavailable')
      : Response.json({ code: failure === 503 ? 'roster_unavailable' : 'role_unavailable', detail: 'Directory unavailable' }, { status: failure }));
    assert.equal(await request, false);
    assert.equal(controller.getState().playerOptionsStatus, 'error');
    assert.deepEqual(controller.getState().playerOptions, []);
    assert.ok(controller.getState().playerOptionsError);
    if (failure !== 'network') assert.equal(controller.getState().playerOptionsError, 'Directory unavailable');
    assert.deepEqual(controller.getState().user, profile);
    assert.equal(await controller.loadPlayerOptions(), true);
    assert.equal(controller.getState().playerOptionsStatus, 'ready');
    assert.deepEqual(controller.getState().playerOptions, []);
    assert.equal(controller.getState().playerOptionsError, null);
    assert.deepEqual(controller.getState().user, profile);
  }
});

test('expired player picker requests clear the session and anonymous or revoked identities cannot load', async () => {
  const { controller, requests, values } = setup(({ path }) => path.endsWith('/players-list')
    ? Response.json({ detail: 'Session expired', code: 'session_expired' }, { status: 401 }) : Response.json(identity('fan')),
  { mobile_token: 'fan-token' });
  await controller.bootstrap();
  assert.equal(await controller.loadPlayerOptions(), false);
  assert.equal(controller.getState().status, 'anonymous');
  assert.equal(controller.getState().user, null);
  assert.equal(controller.getState().playerOptionsStatus, 'idle');
  assert.deepEqual(controller.getState().playerOptions, []);
  assert.equal(values.has('mobile_token'), false);
  assert.equal(await controller.loadPlayerOptions(), false);
  assert.equal(requests.length, 2);
  const revoked = setup(() => Response.json(identity('parent', { current_role_available: false })), { mobile_token: 'revoked-token' });
  await revoked.controller.bootstrap();
  assert.equal(await revoked.controller.loadPlayerOptions(), false);
  assert.equal(revoked.requests.length, 1);
});

test('old picker success or failure cannot populate another role or a replacement session', { timeout: 5000 }, async () => {
  for (const transition of ['logout', 'new-session', 'switch-role']) {
    for (const responseStatus of [200, 401, 503]) {
      let complete;
      let signal;
      const pending = new Promise((resolve) => { signal = resolve; });
      const previous = identity('fan');
      const current = identity('parent', { id: '22222222-2222-4222-8222-222222222222' });
      const { controller, values } = setup(({ path, authorization }) => {
        if (path.endsWith('/players-list')) return new Promise((resolve) => { complete = resolve; signal(); });
        if (path.endsWith('/switch-role')) return Response.json({ token: 'parent-token', user: current, role: 'parent', available_roles: ['parent', 'fan'] });
        return Response.json(authorization === 'Bearer parent-token' ? current : previous);
      }, { mobile_token: 'fan-token' });
      await controller.bootstrap();
      const request = controller.loadPlayerOptions();
      await pending;
      if (transition === 'switch-role') assert.equal(await controller.switchRole('parent'), true);
      else {
        controller.logout();
        if (transition === 'new-session') { values.set('mobile_token', 'parent-token'); await controller.bootstrap(); }
      }
      assert.deepEqual(controller.getState().playerOptions, []);
      assert.equal(controller.getState().playerOptionsStatus, 'idle');
      complete(Response.json(responseStatus === 200 ? { players: playerOptions }
        : { detail: 'Old directory failure', code: 'old_failure' }, { status: responseStatus }));
      assert.equal(await request, false);
      assert.deepEqual(controller.getState().playerOptions, []);
      assert.equal(controller.getState().playerOptionsStatus, 'idle');
      assert.equal(controller.getState().playerOptionsError, null);
      assert.deepEqual(controller.getState().user, transition === 'logout' ? null : current);
      assert.equal(values.get('mobile_token'), transition === 'logout' ? undefined : 'parent-token');
    }
  }
});

test('loaded picker options are invalidated on role changes, failed switching and restored same-role sessions', async () => {
  for (const transition of ['switch-role', 'failed-switch', 'restore']) {
    const previous = identity('fan');
    const current = identity('parent');
    const { controller, values } = setup(({ path, authorization }) => {
      if (path.endsWith('/players-list')) return Response.json({ players: playerOptions });
      if (path.endsWith('/switch-role')) return transition === 'failed-switch'
        ? Response.json({ detail: 'Switch failed' }, { status: 503 })
        : Response.json({ token: 'parent-token', user: current, role: 'parent', available_roles: ['parent', 'fan'] });
      return Response.json(authorization === 'Bearer parent-token' ? current : previous);
    }, { mobile_token: 'fan-token' });
    await controller.bootstrap();
    await controller.loadPlayerOptions();
    assert.deepEqual(controller.getState().playerOptions, playerOptions);
    if (transition === 'restore') { values.set('mobile_token', 'fresh-fan-token'); await controller.bootstrap(); }
    else await controller.switchRole('parent');
    assert.deepEqual(controller.getState().playerOptions, []);
    assert.equal(controller.getState().playerOptionsStatus, 'idle');
    assert.equal(controller.getState().playerOptionsError, null);
  }
});

test('favourite-player save and explicit empty-string clear use the current own identity without a role change', async () => {
  for (const role of ['fan', 'parent', 'player', 'coach', 'management']) {
    let profile = identity(role, { favourite_player_id: 'unlisted-saved-id' });
    const { controller, requests, values } = setup(({ path, method, body }) => {
      if (path.endsWith('/organization')) return Response.json({ available_organizations: [{ id: 'club', name: 'Club' }],
        organization_id: 'club', organization_name: 'Club', requires_organization_selection: false });
      if (path.endsWith('/players-list')) return Response.json({ players: playerOptions });
      if (method === 'PUT') profile = { ...profile, favourite_player_id: body.favourite_player_id || null };
      return Response.json(profile);
    }, { mobile_token: `${role}-token` });
    await controller.bootstrap();
    await controller.loadPlayerOptions();
    for (const selected of ['player-option-2', '']) {
      assert.equal(await controller.updateProfile({ favourite_player_id: selected }), true);
      assert.deepEqual(requests.at(-1).body, { favourite_player_id: selected });
      assert.equal(requests.at(-1).path, '/api/mobile/profile');
      assert.equal(requests.at(-1).authorization, `Bearer ${role}-token`);
      assert.equal(controller.getState().user.favourite_player_id, selected || null);
      assert.deepEqual(controller.getState().playerOptions, playerOptions);
      assert.equal(controller.getState().playerOptionsStatus, 'ready');
      assert.equal(values.get('mobile_token'), `${role}-token`);
      assert.equal(requests.some(({ path }) => path.endsWith('/switch-role')), false);
      if (role === 'management') assert.equal(controller.getState().organization.organization_id, 'club');
    }
  }
});

test('failed favourite-player writes retain the saved reference and loaded directory without optimistic changes', async () => {
  for (const failure of [422, 503, 'network']) {
    const profile = identity('fan', { favourite_player_id: 'saved-id' });
    const { controller, values } = setup(({ path, method }) => {
      if (path.endsWith('/players-list')) return Response.json({ players: playerOptions });
      if (method !== 'PUT') return Response.json(profile);
      if (failure === 'network') throw new TypeError('Network unavailable');
      return Response.json({ detail: 'Favourite could not be saved', code: 'invalid_profile' }, { status: failure });
    }, { mobile_token: 'fan-token' });
    await controller.bootstrap();
    await controller.loadPlayerOptions();
    for (const selection of ['player-option-2', '']) {
      assert.equal(await controller.updateProfile({ favourite_player_id: selection }), false);
      assert.deepEqual(controller.getState().user, profile);
      assert.deepEqual(controller.getState().playerOptions, playerOptions);
      assert.equal(controller.getState().playerOptionsStatus, 'ready');
      assert.ok(controller.getState().error);
      assert.equal(values.get('mobile_token'), 'fan-token');
    }
  }
});

test('picker 401 invalidates pending own writes and organization requests before their late success', { timeout: 5000 }, async (t) => {
  for (const operation of ['profile', 'favorite-team', 'avatar-post', 'avatar-me', 'organization-refresh', 'organization-select']) {
    await t.test(operation, async () => {
      const picker = Promise.withResolvers();
      const pickerStarted = Promise.withResolvers();
      const work = Promise.withResolvers();
      const workStarted = Promise.withResolvers();
      const management = operation.startsWith('organization-');
      const profile = identity(management ? 'management' : 'fan', { favourite_player_id: 'saved-id' });
      const organization = { available_organizations: [{ id: 'club', name: 'Club' }],
        organization_id: 'club', organization_name: 'Club', requires_organization_selection: false };
      const updated = { ...profile, name: 'Late private profile', avatar_url: '/api/uploads/avatars/new.jpg' };
      let posted = false;
      let organizationReads = 0;
      const { controller, values } = setup(({ path, method }) => {
        if (path.endsWith('/players-list')) { pickerStarted.resolve(); return picker.promise; }
        if (path.endsWith('/organization')) {
          if (method === 'GET' && ++organizationReads === 1) return Response.json(organization);
          workStarted.resolve();
          return work.promise;
        }
        if (method === 'POST' && path.endsWith('/avatar')) {
          posted = true;
          if (operation === 'avatar-me') return Response.json({ avatar_url: updated.avatar_url });
          workStarted.resolve();
          return work.promise;
        }
        if (method === 'PUT' || posted) { workStarted.resolve(); return work.promise; }
        return Response.json(profile);
      }, { mobile_token: 'current-token', mobile_available_roles: '["fan"]' });
      await controller.bootstrap();
      const pendingPicker = controller.loadPlayerOptions();
      await pickerStarted.promise;
      const pendingWork = operation === 'profile' ? controller.updateProfile({ name: updated.name })
        : operation === 'favorite-team' ? controller.updateFavoriteTeam('both')
        : operation.startsWith('avatar-') ? controller.uploadAvatar(avatarFile())
        : operation === 'organization-refresh' ? controller.refreshOrganization() : controller.selectOrganization('club');
      await workStarted.promise;
      assert.equal(controller.getState().busy, true);
      picker.resolve(Response.json({ detail: 'Session expired', code: 'session_expired' }, { status: 401 }));
      assert.equal(await pendingPicker, false);
      assert.equal(controller.getState().status, 'anonymous');
      const busyAfterExpiry = controller.getState().busy;
      work.resolve(Response.json(operation === 'favorite-team' ? { ok: true, user: updated }
        : operation === 'avatar-post' ? { avatar_url: updated.avatar_url }
        : management ? organization : updated));
      assert.equal(await pendingWork, false, 'The pending action must be invalidated by session expiry.');
      assert.equal(controller.getState().status, 'anonymous', 'A late success must not restore authenticated private state.');
      assert.equal(controller.getState().user, null);
      assert.equal(controller.getState().organization, null);
      assert.equal(controller.getState().busy, false);
      assert.equal(busyAfterExpiry, false, 'Expired sessions must immediately release the busy state.');
      assert.deepEqual(controller.getState().playerOptions, []);
      assert.equal(controller.getState().playerOptionsStatus, 'idle');
      assert.equal(controller.getState().playerOptionsError, null);
      assert.equal(controller.getState().error, 'Session expired');
      assert.equal(values.has('mobile_token'), false);
      assert.equal(values.has('mobile_available_roles'), false);
      assert.equal(await controller.updateProfile({ name: 'No session' }), false);
    });
  }
});
