import test from 'node:test';
import assert from 'node:assert/strict';
import { createClubEditorController } from '../src/lib/admin/club-controller.ts';
import { createAdminAuthController } from '../src/lib/admin/controller.ts';

const FC = 'lefteria_fc'; const ACADEMY = 'lefteria_fc_academy';
function user(patch = {}) { return { id: '11111111-1111-4111-8111-111111111111', username: 'fixture', name: 'Fixture', role: 'manager',
  permissions: ['settings:read', 'settings:write'], organizations: [{ id: FC, name: 'Lefteria FC' }, { id: ACADEMY, name: 'Lefteria FC Academy' }], ...patch }; }
function profile(org = FC, patch = {}, version = 3) { return { version, club: { id: org === FC ? 'club-profile' : 'club-profile:lefteria_fc_academy',
  organization_id: org, name: org === FC ? 'Fixture FC' : 'Fixture Academy', greek_name: 'Δοκιμαστικός σύλλογος', founded: 2024,
  description: 'Recorded description', motto: null, history: 'Recorded history', logo_url: null, stadium_image_url: null,
  city: 'Λεμεσός', country: 'Κύπρος', address: null, email: null, phone: null, website: null,
  social_links: { facebook: 'https://example.invalid/fixture' }, ...patch } }; }
function setup(handler = request => Response.json(profile(request.organization)), options = {}) {
  const requests = []; let denied = 0; let unauthenticated = 0; let confirmations = 0;
  const controller = createClubEditorController({ baseUrl: 'https://preview.invalid',
    onUnauthenticated: () => { unauthenticated++; }, onForbidden: () => { denied++; },
    confirmDiscard: () => { confirmations++; return options.discard ?? false; }, ...options,
    fetch: async request => {
      const row = { method: request.method, path: new URL(request.url).pathname, organization: new URL(request.url).searchParams.get('organization_id'),
        credentials: request.credentials, cache: request.cache, authorization: request.headers.get('Authorization'), marker: request.headers.get('X-Lefteria-Admin'),
        contentType: request.headers.get('Content-Type'), body: request.body ? await request.clone().json() : undefined };
      requests.push(row); return handler(row, requests);
    } });
  return { controller, requests, denied: () => denied, unauthenticated: () => unauthenticated, confirmations: () => confirmations };
}

test('club editor denies missing settings permission, absent grants and an unassigned query before loading', async () => {
  for (const [authority, initialOrganization] of [[user({ permissions: ['news:*'] }), null], [user({ organizations: [] }), null],
    [user({ organizations: [{ id: ACADEMY, name: 'Academy' }] }), FC], [user(), 'other']]) {
    const { controller, requests } = setup(undefined, { initialOrganization });
    await controller.setAuthority(authority);
    assert.equal(controller.getState().status, 'forbidden'); assert.equal(requests.length, 0);
  }
});

test('club loading keeps the imported version and uses only assigned explicit organization and same-origin cookies', async () => {
  const { controller, requests } = setup();
  await controller.setAuthority(user());
  assert.equal(controller.getState().profile.version, 3);
  assert.equal(controller.getState().draft.name, 'Fixture FC');
  assert.equal(controller.getState().dirty, false);
  assert.deepEqual(requests[0], { method: 'GET', path: '/api/admin/club', organization: FC, credentials: 'same-origin', cache: 'no-store',
    authorization: null, marker: null, contentType: null, body: undefined });
  await controller.selectOrganization(ACADEMY);
  assert.equal(controller.getState().draft.name, 'Fixture Academy');
  assert.equal(requests[1].organization, ACADEMY);
});

test('club editing cancels without mutating the loaded baseline and prompts only for a real dirty organization switch', async () => {
  const { controller, requests, confirmations } = setup();
  await controller.setAuthority(user()); controller.edit(); controller.change('name', 'Unsaved fixture');
  assert.equal(controller.getState().profile.club.name, 'Fixture FC');
  assert.equal(await controller.selectOrganization(ACADEMY), false);
  assert.equal(confirmations(), 1); assert.equal(controller.getState().draft.name, 'Unsaved fixture');
  assert.equal(requests.length, 1);
  controller.cancel(); assert.equal(controller.getState().dirty, false);
  assert.equal(controller.getState().draft.name, 'Fixture FC');
  await controller.selectOrganization(ACADEMY); assert.equal(confirmations(), 1);
});

test('confirmed dirty organization switch discards only the old form and does not carry its version into the new owner', async () => {
  const { controller } = setup(request => Response.json(profile(request.organization, {}, request.organization === FC ? 3 : 9)), { discard: true });
  await controller.setAuthority(user()); controller.edit(); controller.change('history', 'Unsaved private draft');
  assert.equal(await controller.selectOrganization(ACADEMY), true);
  assert.equal(controller.getState().profile.version, 9); assert.equal(controller.getState().dirty, false);
  assert.equal(controller.getState().draft.history, 'Recorded history');
});

test('club save sends exactly the safe editable fields and expected version, then adopts the confirmed response', async () => {
  const { controller, requests } = setup(request => Response.json(request.method === 'GET'
    ? profile(FC, { bank_iban: 'PRIVATE-FIXTURE', twilio_auth_token: 'PRIVATE-FIXTURE' }) : profile(FC, request.body.club, 4)));
  await controller.setAuthority(user()); controller.edit(); controller.change('name', 'Edited fixture');
  controller.change('history', 'Ιστορία '.repeat(12500).slice(0, 100000));
  controller.changeSocial('instagram', 'https://example.invalid/club');
  assert.equal(await controller.save(), true);
  const request = requests[1];
  assert.equal(request.method, 'PUT'); assert.equal(request.organization, FC);
  assert.equal(request.credentials, 'same-origin'); assert.equal(request.marker, '1'); assert.equal(request.contentType, 'application/json');
  assert.equal(request.authorization, null); assert.equal(request.body.expected_version, 3);
  assert.equal(request.body.club.history.length, 100000);
  assert.deepEqual(Object.keys(request.body.club).sort(), ['name', 'greek_name', 'founded', 'description', 'motto', 'history', 'logo_url',
    'stadium_image_url', 'city', 'country', 'address', 'email', 'phone', 'website', 'social_links'].sort());
  assert.doesNotMatch(JSON.stringify(request.body), /PRIVATE-FIXTURE|bank_iban|twilio_auth_token|organization_id|club-profile/);
  assert.equal(controller.getState().profile.version, 4); assert.equal(controller.getState().editing, false);
  assert.equal(controller.getState().dirty, false); assert.ok(controller.getState().success);
});

test('club 409 keeps the draft, prevents overwrite/retry and requires an explicit latest-version reload', async () => {
  let version = 3;
  const { controller, requests } = setup(request => request.method === 'PUT'
    ? Response.json({ code: 'club_version_conflict' }, { status: 409 }) : Response.json(profile(FC, {}, version)));
  await controller.setAuthority(user()); controller.edit(); controller.change('name', 'Unsaved conflict');
  assert.equal(await controller.save(), false); assert.equal(controller.getState().conflict, true);
  assert.equal(controller.getState().draft.name, 'Unsaved conflict'); assert.equal(controller.getState().profile.version, 3);
  assert.equal(await controller.save(), false); assert.equal(requests.length, 2);
  version = 8; await controller.load();
  assert.equal(controller.getState().profile.version, 8); assert.equal(controller.getState().dirty, false);
  assert.equal(controller.getState().conflict, false); assert.equal(controller.getState().draft.name, 'Fixture FC');
});

test('club validation and unavailable saves preserve the draft and never report success', async () => {
  for (const status of [422, 503, 500, 404]) {
    const { controller } = setup(request => request.method === 'GET' ? Response.json(profile()) : Response.json({ detail: 'PRIVATE-FIXTURE' }, { status }));
    await controller.setAuthority(user()); controller.edit(); controller.change('name', 'Unsaved fixture');
    assert.equal(await controller.save(), false); assert.equal(controller.getState().draft.name, 'Unsaved fixture');
    assert.equal(controller.getState().editing, true); assert.equal(controller.getState().dirty, true);
    assert.equal(controller.getState().success, null); assert.doesNotMatch(controller.getState().error, /PRIVATE-FIXTURE/);
  }
});

test('club 401 clears every private field and 403 clears access before refreshing current authorization', async () => {
  for (const status of [401, 403]) {
    const { controller, unauthenticated, denied } = setup(request => request.method === 'GET' ? Response.json(profile()) : Response.json({}, { status }));
    await controller.setAuthority(user()); controller.edit(); controller.change('name', 'Secret draft');
    await controller.save();
    assert.equal(controller.getState().profile, null); assert.equal(controller.getState().draft, null);
    assert.equal(controller.getState().dirty, false); assert.equal(controller.getState().saving, false);
    assert.equal(unauthenticated(), status === 401 ? 1 : 0); assert.equal(denied(), status === 403 ? 1 : 0);
  }
});

test('club read-only permissions block editing and current revoked grants remove all old club data', async () => {
  const { controller, requests } = setup();
  await controller.setAuthority(user({ permissions: ['settings:read'] })); controller.edit(); controller.change('name', 'Forbidden');
  assert.equal(controller.getState().editing, false); assert.equal(await controller.save(), false); assert.equal(requests.length, 1);
  await controller.setAuthority(user({ organizations: [] }));
  assert.equal(controller.getState().status, 'forbidden'); assert.equal(controller.getState().draft, null);
});

test('club missing record, unavailable storage and invalid/wrong-owner versions have distinct fail-closed states', async () => {
  for (const [response, expected] of [[Response.json({}, { status: 404 }), 'not_found'], [Response.json({}, { status: 503 }), 'unavailable'],
    [Response.json(profile(ACADEMY)), 'unavailable'], [Response.json(profile(FC, {}, Number.MAX_SAFE_INTEGER + 1)), 'unavailable']]) {
    const { controller } = setup(() => response);
    await controller.setAuthority(user()); assert.equal(controller.getState().status, expected);
    assert.equal(controller.getState().profile, null); assert.equal(controller.getState().draft, null);
  }
});

test('late club loads cannot cross an organization switch or restore state after logout', async () => {
  let release;
  const delayed = new Promise(resolve => { release = resolve; });
  const { controller } = setup(request => request.organization === FC ? delayed : Response.json(profile(ACADEMY)));
  const loading = controller.setAuthority(user());
  await controller.selectOrganization(ACADEMY); release(Response.json(profile(FC))); await loading;
  assert.equal(controller.getState().profile.club.organization_id, ACADEMY);
  await controller.setAuthority(null); assert.equal(controller.getState().profile, null); assert.equal(controller.getState().draft, null);
});

test('late club save after losing the authenticated identity cannot restore or announce saved data', async () => {
  let release;
  const delayed = new Promise(resolve => { release = resolve; });
  const { controller } = setup(request => request.method === 'GET' ? Response.json(profile()) : delayed);
  await controller.setAuthority(user()); controller.edit(); controller.change('name', 'Pending save');
  const saving = controller.save(); await controller.setAuthority(null);
  release(Response.json(profile(FC, { name: 'Pending save' }, 4))); await saving;
  assert.equal(controller.getState().profile, null); assert.equal(controller.getState().draft, null); assert.equal(controller.getState().success, null);
});

test('club write permission revoked during a save removes editing and ignores the old response', async () => {
  let release;
  const delayed = new Promise(resolve => { release = resolve; });
  const { controller } = setup(request => request.method === 'GET' ? Response.json(profile()) : delayed);
  await controller.setAuthority(user()); controller.edit(); controller.change('name', 'Pending save');
  const saving = controller.save(); await controller.setAuthority(user({ permissions: ['settings:read'] }));
  assert.equal(controller.getState().saving, false); assert.equal(controller.getState().editing, false);
  assert.equal(controller.getState().draft.name, 'Fixture FC');
  release(Response.json(profile(FC, { name: 'Pending save' }, 4))); await saving;
  assert.equal(controller.getState().profile.version, 3); assert.equal(controller.getState().success, null);
});

test('club 401 invalidation defeats a delayed auth refresh and clears the existing auth session', async () => {
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const auth = createAdminAuthController({ baseUrl: 'https://fixture.invalid', now: () => 0, fetch: async () => pending });
  const checking = auth.bootstrap(); auth.invalidateSession();
  release(Response.json({ user: user(), expires_at: '2099-01-01T00:00:00Z' })); await checking;
  assert.equal(auth.getState().status, 'anonymous'); assert.equal(auth.getState().session, null);
});
