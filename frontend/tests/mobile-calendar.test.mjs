import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';
import { calendarItemKey, calendarItemOnDate, calendarMonthDays, calendarWeekDays, clubToday,
  formatCalendarDate, isCalendarDate, safeCalendarLink, shiftCalendarMonth } from '../src/lib/mobile/calendar.ts';

const identityId = '11111111-1111-4111-8111-111111111111';
const players = [{ id: 'first-child', name: 'First child', organization_id: 'fc' },
  { id: 'second-child', name: 'Second child', organization_id: 'academy' }];
function identity(role = 'parent', patch = {}) {
  return { id: identityId, phone: '+35799123456', role, name: 'Own identity', platform: 'Web/PWA',
    created_at: '2026-01-01T00:00:00Z', last_login: '2026-10-29T00:00:00Z',
    available_roles: [role, 'fan'], current_role_available: true, ...patch };
}
function organization(id = 'fc') {
  return { organization_id: id, organization_name: id, available_organizations: [{ id: 'fc', name: 'FC' }, { id: 'academy', name: 'Academy' }],
    requires_organization_selection: id === null };
}
function item(patch = {}) {
  return { id: 'shared-id', kind: 'training', event_type: 'training', organization_id: 'fc', title: 'Real training',
    date: '2026-10-01', end_date: '2026-10-01', start_time: '18:30', starts_at: '2026-10-01T15:30:00Z',
    ends_at: null, arrival_time: '18:10', duration_minutes: 75, location: 'Club ground', location_url: 'https://maps.example/club',
    description: null, member_summary: 'Published member summary', session_journey: null, preparation: 'Bring water',
    status: 'scheduled', is_cancelled: false, cancel_reason: null, content_status: 'published', responses: [], ...patch };
}
function calendar(role = 'parent', patch = {}) {
  return { identity_id: identityId, role, month: '2026-10', time_zone: 'Asia/Nicosia',
    organization_id: role === 'coach' ? null : 'fc', players: role === 'parent' ? players : role === 'player' ? [players[0]] : [],
    selected_player_id: role === 'parent' || role === 'player' ? players[0].id : null, items: [item()], next_offset: null, ...patch };
}
function replies(patch = {}) {
  return { event_id: 'shared-id', event_type: 'training', organization_id: 'fc',
    players: [{ id: 'own-scope-player', name: 'Scoped player', number: 4, position: 'Defender', image_url: null, status: 'maybe', updated_at: null }],
    next_offset: null, ...patch };
}
function setup(handler, role = 'parent') {
  const values = new Map([['mobile_token', `${role}-token`]]);
  const requests = [];
  const storage = createMobileSessionStorage({ getItem: (key) => values.get(key) ?? null,
    setItem: (key, value) => values.set(key, value), removeItem: (key) => values.delete(key) });
  const controller = createMobileAuthController({ baseUrl: 'https://club.example', storage,
    fetch: async (request) => {
      const url = new URL(request.url);
      const call = { path: url.pathname, query: Object.fromEntries(url.searchParams), method: request.method,
        authorization: request.headers.get('authorization'), cache: request.cache,
        body: request.body ? await request.json() : undefined };
      requests.push(call);
      const result = await handler(call, requests);
      if (result !== undefined) return result;
      if (call.path.endsWith('/organization')) return Response.json(organization());
      if (call.path.endsWith('/me')) return Response.json(identity(role));
      throw new Error(`Unexpected request ${call.method} ${call.path}`);
    }
  });
  return { controller, values, requests };
}
const failure = (status = 503) => Response.json({ detail: 'Calendar unavailable', code: 'calendar_unavailable' }, { status });

test('all four member roles load real scoped calendars; fan and missing organization do not request private data', async () => {
  for (const role of ['parent', 'player', 'coach', 'management']) {
    const { controller, requests } = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(calendar(role)) : undefined, role);
    await controller.bootstrap();
    assert.equal(await controller.loadCalendar('2026-10'), true);
    assert.deepEqual(controller.getState().calendar, calendar(role));
    assert.deepEqual(requests.at(-1), { path: '/api/mobile/calendar', method: 'GET', query: { month: '2026-10', offset: '0' },
      authorization: `Bearer ${role}-token`, cache: 'no-store', body: undefined });
  }
  for (const role of ['fan', 'management']) {
    const { controller, requests } = setup(({ path }) => path.endsWith('/organization') ? Response.json(organization(null)) : undefined, role);
    await controller.bootstrap();
    const before = requests.length;
    assert.equal(await controller.loadCalendar('2026-10'), false);
    assert.equal(requests.length, before);
    assert.equal(controller.getState().calendar, null);
  }
});

test('parent explicitly selects second child and RSVP payload includes child and kind; state comes from fresh GET', async () => {
  const refresh = Promise.withResolvers();
  const refreshing = Promise.withResolvers();
  let saved = false;
  const second = calendar('parent', { selected_player_id: 'second-child', organization_id: 'academy',
    items: [item({ organization_id: 'academy' })] });
  const { controller, requests } = setup(({ path, method, body, query }) => {
    if (path === '/api/mobile/availability') {
      assert.deepEqual(body, { event_id: 'shared-id', event_type: 'training', player_id: 'second-child', status: 'going' });
      saved = true;
      return Response.json({ message: 'Saved', ...body, status: 'maybe' });
    }
    if (path === '/api/mobile/calendar') {
      if (saved) { refreshing.resolve(); return refresh.promise; }
      return Response.json(query.player_id === 'second-child' ? second : calendar());
    }
  });
  await controller.bootstrap();
  await controller.loadCalendar('2026-10');
  assert.equal(await controller.loadCalendar('2026-10', 'second-child'), true);
  const writing = controller.submitAvailability(controller.getState().calendar.items[0], 'going');
  await refreshing.promise;
  assert.equal(controller.getState().calendar, null, 'No optimistic response is substituted for fresh server data.');
  refresh.resolve(Response.json({ ...second, items: [item({ organization_id: 'academy', responses: [{ player_id: 'second-child', status: 'maybe', updated_at: null }] })] }));
  assert.equal(await writing, true);
  assert.equal(controller.getState().calendar.items[0].responses[0].status, 'maybe');
  assert.deepEqual(requests.at(-1).query, { month: '2026-10', offset: '0', player_id: 'second-child' });
});

test('cancelled events and staff views cannot write RSVP; past member events remain eligible', async () => {
  for (const role of ['parent', 'player', 'coach', 'management']) {
    const cancelled = calendar(role, { items: [item({ is_cancelled: true, cancel_reason: 'Weather' })] });
    const { controller, requests } = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(cancelled) : undefined, role);
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    assert.equal(await controller.submitAvailability(cancelled.items[0], 'going'), false);
    assert.equal(requests.some(({ method }) => method === 'POST'), false);
    if (role === 'coach' || role === 'management') {
      cancelled.items[0].is_cancelled = false;
      await controller.loadCalendar('2026-10');
      assert.equal(await controller.submitAvailability(cancelled.items[0], 'maybe'), false);
    }
  }
  let writes = 0;
  const { controller } = setup(({ path, body }) => path === '/api/mobile/calendar' ? Response.json(calendar())
    : path === '/api/mobile/availability' ? (++writes, Response.json({ message: 'Saved', ...body })) : undefined);
  await controller.bootstrap(); await controller.loadCalendar('2026-10');
  assert.equal(await controller.submitAvailability(controller.getState().calendar.items[0], 'maybe'), true);
  assert.equal(writes, 1, 'The club permits RSVP on past events.');
});

test('unavailable initial calendar stays an error with explicit retry; only a verified 200 is empty', async () => {
  let attempts = 0;
  const { controller } = setup(({ path }) => path === '/api/mobile/calendar'
    ? ++attempts === 1 ? failure() : Response.json(calendar('parent', { items: [] })) : undefined);
  await controller.bootstrap();
  assert.equal(await controller.loadCalendar('2026-10'), false);
  assert.equal(controller.getState().calendarStatus, 'error');
  assert.equal(controller.getState().calendar, null);
  assert.equal(controller.getState().calendarError, 'Calendar unavailable');
  assert.equal(await controller.loadCalendar('2026-10'), true);
  assert.equal(controller.getState().calendarStatus, 'ready');
  assert.deepEqual(controller.getState().calendar.items, []);
});

test('month pagination requests real offsets, deduplicates by kind/id and retains rows on page errors', async () => {
  let failPage = true;
  const { controller, requests } = setup(({ path, query }) => {
    if (path !== '/api/mobile/calendar') return;
    if (query.offset === '0') return Response.json(calendar('parent', { next_offset: 200 }));
    assert.equal(query.offset, '200');
    if (failPage) return failure();
    return Response.json(calendar('parent', { items: [item({ title: 'Refreshed duplicate' }), item({ kind: 'match', event_type: 'match' })] }));
  });
  await controller.bootstrap(); await controller.loadCalendar('2026-10');
  assert.equal(await controller.loadMoreCalendar(), false);
  assert.equal(controller.getState().calendarStatus, 'ready');
  assert.equal(controller.getState().calendar.items.length, 1);
  assert.equal(controller.getState().calendar.next_offset, 200);
  failPage = false;
  assert.equal(await controller.loadMoreCalendar(), true);
  assert.deepEqual(controller.getState().calendar.items.map(calendarItemKey), ['training:shared-id', 'match:shared-id']);
  assert.equal(controller.getState().calendar.items[0].title, 'Refreshed duplicate');
  assert.equal(await controller.loadMoreCalendar(), false);
  assert.deepEqual(requests.at(-1).query, { month: '2026-10', player_id: 'first-child', offset: '200' });
});

test('revoked child on month append 403 clears all earlier private pages and permits a fresh allowed selection', async () => {
  let revoked = false;
  const remaining = calendar('parent', { players: [players[1]], selected_player_id: 'second-child', organization_id: 'academy',
    items: [item({ organization_id: 'academy' })] });
  const { controller } = setup(({ path, query }) => {
    if (path !== '/api/mobile/calendar') return;
    if (query.offset === '200') {
      revoked = true;
      return Response.json({ code: 'invalid_calendar_input', detail: 'Selected child is no longer available' }, { status: 403 });
    }
    return Response.json(revoked ? remaining : calendar('parent', { next_offset: 200 }));
  });
  await controller.bootstrap(); await controller.loadCalendar('2026-10');
  assert.equal(await controller.loadMoreCalendar(), false);
  assert.equal(controller.getState().calendar, null);
  assert.equal(controller.getState().calendarStatus, 'error');
  assert.equal(controller.getState().calendarLoadingMore, false);
  assert.equal(controller.getState().calendarResponses, null);
  assert.equal(controller.getState().calendarResponsesStatus, 'idle');
  assert.equal(controller.getState().user.current_role_available, true, 'Other current children retain the parent role.');
  assert.equal(controller.getState().status, 'authenticated');
  assert.equal(await controller.loadCalendar('2026-10'), true);
  assert.deepEqual(controller.getState().calendar, remaining);
});

test('malformed or mismatched identity, role, month, child, organization and calendar rows never populate state', async () => {
  const invalid = [
    { identity_id: 'different-identity' }, { role: 'coach' }, { month: '2026-11' }, { selected_player_id: 'unknown' },
    { organization_id: 'wrong-org' }, { time_zone: 'Invalid/Zone' }, { next_offset: 0 },
    { items: [item({ organization_id: 'other-org' })] }, { items: [item({ date: '2026-02-30' })] },
    { items: [item({ responses: [{ player_id: 'second-child', status: 'going', updated_at: null }] })] },
    { items: [item({ description: { private: 'wrong shape' } })] }, { players: [players[0], players[0]] }
  ];
  for (const patch of invalid) {
    const { controller } = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(calendar('parent', patch)) : undefined);
    await controller.bootstrap();
    assert.equal(await controller.loadCalendar('2026-10'), false, JSON.stringify(patch));
    assert.equal(controller.getState().calendar, null);
    assert.equal(controller.getState().calendarStatus, 'error');
  }
});

test('late calendar success and failures cannot overwrite a new child, month or pagination selection', async () => {
  for (const transition of ['month', 'child', 'page']) for (const status of [200, 401, 403, 503]) {
    const old = Promise.withResolvers();
    const started = Promise.withResolvers();
    let waiting = false;
    const current = calendar('parent', transition === 'month' || transition === 'page' ? { month: '2026-11', items: [item({ date: '2026-11-01', end_date: '2026-11-01' })] }
      : { selected_player_id: 'second-child', organization_id: 'academy', items: [item({ organization_id: 'academy' })] });
    const { controller } = setup(({ path, query }) => {
      if (path !== '/api/mobile/calendar') return;
      if (waiting && (query.month === '2026-11' || query.player_id === 'second-child')) return Response.json(current);
      if (transition === 'page' && query.offset === '0') return Response.json(calendar('parent', { next_offset: 200 }));
      waiting = true; started.resolve(); return old.promise;
    });
    await controller.bootstrap();
    if (transition === 'page') await controller.loadCalendar('2026-10');
    const pending = transition === 'page' ? controller.loadMoreCalendar() : controller.loadCalendar('2026-10');
    await started.promise;
    assert.equal(controller.getState().busy, false);
    await controller.loadCalendar(current.month, current.selected_player_id);
    old.resolve(status === 200 ? Response.json(calendar()) : failure(status));
    assert.equal(await pending, false);
    assert.deepEqual(controller.getState().calendar, current);
    assert.equal(controller.getState().calendarError, null);
    assert.equal(controller.getState().status, 'authenticated');
  }
});

test('role, organization, logout, new bootstrap and revoked current identities invalidate late calendar responses', async () => {
  for (const transition of ['role', 'organization', 'logout', 'bootstrap', 'revoked']) for (const status of [200, 401, 403, 503]) {
    const old = Promise.withResolvers();
    const started = Promise.withResolvers();
    let restored = false;
    const role = transition === 'organization' ? 'management' : 'parent';
    const { controller, values } = setup(({ path, method }) => {
      if (path === '/api/mobile/calendar') { started.resolve(); return old.promise; }
      if (path.endsWith('/switch-role')) return Response.json({ token: 'coach-token', user: identity('coach'), role: 'coach', available_roles: ['coach', 'fan'] });
      if (path.endsWith('/organization') && method === 'POST') return Response.json(organization('academy'));
      if (path.endsWith('/me') && restored) return Response.json(identity(role, { current_role_available: transition !== 'revoked' }));
    }, role);
    await controller.bootstrap();
    const pending = controller.loadCalendar('2026-10'); await started.promise;
    if (transition === 'role') await controller.switchRole('coach');
    else if (transition === 'organization') await controller.selectOrganization('academy');
    else if (transition === 'logout') controller.logout();
    else { restored = true; values.set('mobile_token', 'fresh-token'); await controller.bootstrap(); }
    old.resolve(status === 200 ? Response.json(calendar(role)) : failure(status));
    assert.equal(await pending, false);
    assert.equal(controller.getState().calendar, null);
    assert.equal(controller.getState().calendarStatus, 'idle');
    assert.equal(controller.getState().calendarError, null);
    assert.equal(controller.getState().status, transition === 'logout' ? 'anonymous' : 'authenticated');
    if (transition === 'organization') assert.equal(controller.getState().organization.organization_id, 'academy');
    if (transition === 'revoked') assert.equal(controller.getState().user.current_role_available, false);
  }
});

test('current calendar 401 invalidates an in-flight own write and releases busy without accepting its late success', async () => {
  const read = Promise.withResolvers(); const reading = Promise.withResolvers();
  const write = Promise.withResolvers(); const writing = Promise.withResolvers();
  const { controller, values } = setup(({ path, method }) => {
    if (path === '/api/mobile/calendar') { reading.resolve(); return read.promise; }
    if (method === 'PUT') { writing.resolve(); return write.promise; }
  });
  await controller.bootstrap();
  const pendingRead = controller.loadCalendar('2026-10'); await reading.promise;
  const pendingWrite = controller.updateProfile({ name: 'Late name' }); await writing.promise;
  read.resolve(failure(401));
  assert.equal(await pendingRead, false);
  assert.equal(controller.getState().busy, false);
  assert.equal(controller.getState().status, 'anonymous');
  write.resolve(Response.json(identity('parent', { name: 'Late name' })));
  assert.equal(await pendingWrite, false);
  assert.equal(controller.getState().user, null);
  assert.equal(controller.getState().calendar, null);
  assert.equal(values.has('mobile_token'), false);
});

test('current account-unavailable 403 revokes the role, clears private state and preserves explicit fan recovery', async () => {
  for (const operation of ['calendar', 'responses', 'write']) {
    let revoked = false;
    const role = operation === 'responses' ? 'coach' : 'parent';
    const unavailable = () => Response.json({ code: 'account_unavailable', detail: 'Current role revoked' }, { status: 403 });
    const { controller, values } = setup(({ path }) => {
      if (path === '/api/mobile/calendar') return revoked ? unavailable() : Response.json(calendar(role));
      if (path.endsWith('/responses') || path === '/api/mobile/availability') return unavailable();
      if (path.endsWith('/switch-role')) return Response.json({ token: 'fan-token', role: 'fan', available_roles: ['fan'], user: identity('fan', { available_roles: ['fan'] }) });
    }, role);
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    revoked = true;
    assert.equal(await (operation === 'calendar' ? controller.loadCalendar('2026-10')
      : operation === 'responses' ? controller.loadCalendarResponses(item()) : controller.submitAvailability(item(), 'going')), false);
    assert.equal(controller.getState().user.current_role_available, false);
    assert.equal(controller.getState().calendar, null);
    assert.equal(controller.getState().calendarResponses, null);
    assert.equal(controller.getState().calendarStatus, 'idle');
    assert.equal(controller.getState().organization, null);
    assert.equal(controller.getState().busy, false);
    assert.equal(values.get('mobile_token'), `${role}-token`);
    assert.equal(await controller.loadCalendar('2026-10'), false);
    assert.equal(await controller.switchRole('fan'), true);
    assert.equal(controller.getState().user.role, 'fan');
  }
});

test('late availability success or failure after logout cannot restore a session or refresh private data', async () => {
  for (const status of [200, 401, 403, 503]) {
    const write = Promise.withResolvers(); const started = Promise.withResolvers();
    const { controller, requests } = setup(({ path }) => {
      if (path === '/api/mobile/calendar') return Response.json(calendar());
      if (path === '/api/mobile/availability') { started.resolve(); return write.promise; }
    });
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    const pending = controller.submitAvailability(controller.getState().calendar.items[0], 'going');
    await started.promise; controller.logout();
    write.resolve(status === 200 ? Response.json({ message: 'Saved', status: 'going', player_id: 'first-child', event_id: 'shared-id', event_type: 'training' }) : failure(status));
    assert.equal(await pending, false);
    assert.equal(controller.getState().status, 'anonymous');
    assert.equal(controller.getState().calendar, null);
    assert.equal(requests.filter(({ path }) => path === '/api/mobile/calendar').length, 1);
  }
});

test('server cancellation, denied writes and failed confirmation never invent an RSVP success', async () => {
  for (const outcome of [409, 403, 503, 'malformed', 'refresh-failed']) {
    let saved = false;
    const before = calendar('parent', { items: [item({ responses: [{ player_id: 'first-child', status: 'maybe', updated_at: null }] })] });
    const { controller, requests } = setup(({ path, body }) => {
      if (path === '/api/mobile/calendar') return saved ? failure() : Response.json(before);
      if (path === '/api/mobile/availability') {
        if (outcome === 'refresh-failed') { saved = true; return Response.json({ message: 'Saved', ...body }); }
        return outcome === 'malformed' ? Response.json({ message: 'Wrong player', ...body, player_id: 'someone-else' })
          : Response.json({ detail: 'Availability rejected', code: 'calendar_cancelled' }, { status: outcome });
      }
    });
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    assert.equal(await controller.submitAvailability(before.items[0], 'going'), false);
    if (outcome === 'refresh-failed') {
      assert.equal(controller.getState().calendar, null);
      assert.equal(controller.getState().calendarStatus, 'error');
      assert.match(controller.getState().error, /αποθηκεύτηκε/);
    } else {
      assert.deepEqual(controller.getState().calendar, before);
      assert.equal(requests.filter(({ path }) => path === '/api/mobile/calendar').length, 1);
    }
    assert.ok(controller.getState().error);
  }
});

test('staff replies use additive kind/id route, scoped roster and pagination; members cannot load replies', async () => {
  for (const role of ['coach', 'management']) {
    const { controller, requests } = setup(({ path, query }) => {
      if (path === '/api/mobile/calendar') return Response.json(calendar(role));
      if (path.endsWith('/responses')) return Response.json(replies(query.offset === '0' ? { next_offset: 200 }
        : { players: [replies().players[0], { ...replies().players[0], id: 'another-scoped-player', status: null }] }));
    }, role);
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    const event = controller.getState().calendar.items[0];
    assert.equal(await controller.loadCalendarResponses(event), true);
    assert.equal(controller.getState().busy, false);
    assert.equal(await controller.loadCalendarResponses(event, true), true);
    assert.equal(controller.getState().calendarResponses.players.length, 2);
    assert.equal(requests.at(-1).path, '/api/mobile/calendar/training/shared-id/responses');
    assert.deepEqual(requests.at(-1).query, { offset: '200' });
  }
  const member = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(calendar()) : undefined);
  await member.controller.bootstrap(); await member.controller.loadCalendar('2026-10');
  assert.equal(await member.controller.loadCalendarResponses(item()), false);
  assert.equal(member.requests.some(({ path }) => path.endsWith('/responses')), false);
});

test('staff can read imported no_response before any RSVP while own writes still accept only three statuses', async () => {
  const importedReplies = replies({ players: [{ ...replies().players[0], status: 'no_response' }] });
  for (const role of ['coach', 'management']) {
    const { controller, requests } = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(calendar(role))
      : path.endsWith('/responses') ? Response.json(importedReplies) : undefined, role);
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    assert.equal(await controller.loadCalendarResponses(item()), true);
    assert.equal(controller.getState().calendarResponsesStatus, 'ready');
    assert.equal(controller.getState().calendarResponses.players[0].status, 'no_response');
    assert.equal(controller.getState().calendarResponsesError, null);
    assert.equal(requests.some(({ method }) => method === 'POST'), false);
  }
  const member = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(calendar()) : undefined);
  await member.controller.bootstrap(); await member.controller.loadCalendar('2026-10');
  assert.equal(await member.controller.submitAvailability(item(), 'no_response'), false);
  assert.equal(member.requests.some(({ method }) => method === 'POST'), false);
});

test('staff append 403 clears prior replies and calendar after assignment loss; temporary 503 retains confirmed pages', async () => {
  for (const status of [403, 503]) {
    const { controller } = setup(({ path, query }) => {
      if (path === '/api/mobile/calendar') return Response.json(calendar('coach'));
      if (path.endsWith('/responses')) return query.offset === '0' ? Response.json(replies({ next_offset: 200 }))
        : Response.json({ code: status === 403 ? 'invalid_calendar_input' : 'calendar_unavailable', detail: 'Reply roster unavailable' }, { status });
    }, 'coach');
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    await controller.loadCalendarResponses(item());
    assert.equal(await controller.loadCalendarResponses(item(), true), false);
    assert.equal(controller.getState().calendarResponsesLoadingMore, false);
    assert.equal(controller.getState().user.current_role_available, true);
    if (status === 403) {
      assert.equal(controller.getState().calendarResponses, null);
      assert.equal(controller.getState().calendarResponsesKey, null);
      assert.equal(controller.getState().calendarResponsesStatus, 'idle');
      assert.equal(controller.getState().calendar, null);
      assert.equal(controller.getState().calendarStatus, 'error');
      assert.equal(controller.getState().calendarError, 'Reply roster unavailable');
    } else {
      assert.deepEqual(controller.getState().calendarResponses, replies({ next_offset: 200 }));
      assert.equal(controller.getState().calendarResponsesStatus, 'ready');
      assert.deepEqual(controller.getState().calendar, calendar('coach'));
      assert.equal(controller.getState().calendarStatus, 'ready');
    }
  }
});

test('staff reply failures end loading and support retry; closed or obsolete replies ignore late 200/401/403/503', async () => {
  let failed = true;
  const available = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(calendar('coach'))
    : path.endsWith('/responses') ? failed ? failure() : Response.json(replies()) : undefined, 'coach');
  await available.controller.bootstrap(); await available.controller.loadCalendar('2026-10');
  assert.equal(await available.controller.loadCalendarResponses(item()), false);
  assert.equal(available.controller.getState().calendarResponsesStatus, 'error');
  failed = false;
  assert.equal(await available.controller.loadCalendarResponses(item()), true);
  for (const change of ['close', 'month', 'logout']) for (const status of [200, 401, 403, 503]) {
    const old = Promise.withResolvers(); const started = Promise.withResolvers();
    const { controller } = setup(({ path, query }) => path === '/api/mobile/calendar'
      ? Response.json(calendar('coach', query.month === '2026-11' ? { month: '2026-11', items: [] } : {}))
      : path.endsWith('/responses') ? (started.resolve(), old.promise) : undefined, 'coach');
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    const pending = controller.loadCalendarResponses(item()); await started.promise;
    if (change === 'close') controller.clearCalendarResponses();
    else if (change === 'month') await controller.loadCalendar('2026-11');
    else controller.logout();
    old.resolve(status === 200 ? Response.json(replies()) : failure(status));
    assert.equal(await pending, false);
    assert.equal(controller.getState().calendarResponses, null);
    assert.equal(controller.getState().calendarResponsesStatus, 'idle');
    assert.equal(controller.getState().status, change === 'logout' ? 'anonymous' : 'authenticated');
  }
});

test('staff replies reject wrong event/kind/organization and current 401 clears every private calendar field', async () => {
  for (const patch of [{ event_id: 'other' }, { event_type: 'match' }, { organization_id: 'academy' },
    { next_offset: 0 }, { players: [{ ...replies().players[0], status: 'present' }] }, 'expired']) {
    const { controller } = setup(({ path }) => path === '/api/mobile/calendar' ? Response.json(calendar('coach'))
      : path.endsWith('/responses') ? patch === 'expired' ? failure(401) : Response.json(replies(patch)) : undefined, 'coach');
    await controller.bootstrap(); await controller.loadCalendar('2026-10');
    assert.equal(await controller.loadCalendarResponses(item()), false);
    assert.equal(controller.getState().calendarResponses, null);
    if (patch === 'expired') {
      assert.equal(controller.getState().status, 'anonymous');
      assert.equal(controller.getState().calendar, null);
      assert.equal(controller.getState().calendarResponsesStatus, 'idle');
    } else assert.equal(controller.getState().calendarResponsesStatus, 'error');
  }
});

test('club date-only and time values survive browser timezones, DST boundaries and Monday-first grids', () => {
  const original = process.env.TZ;
  try {
    for (const zone of ['Pacific/Honolulu', 'Asia/Nicosia', 'Pacific/Kiritimati']) {
      process.env.TZ = zone;
      assert.equal(formatCalendarDate('2026-10-01', { day: 'numeric' }), '1');
      assert.deepEqual(calendarWeekDays('2026-10-01'), ['2026-09-28', '2026-09-29', '2026-09-30', '2026-10-01', '2026-10-02', '2026-10-03', '2026-10-04']);
      assert.deepEqual(calendarMonthDays('2026-10').slice(0, 5), [null, null, null, '2026-10-01', '2026-10-02']);
      assert.equal(clubToday('Asia/Nicosia', Date.parse('2026-10-24T21:30:00Z')), '2026-10-25');
      assert.equal(shiftCalendarMonth('2026-12', 1), '2027-01');
      assert.equal(calendarItemOnDate(item({ end_date: '2026-10-03' }), '2026-10-02'), true);
      assert.equal(isCalendarDate('2026-02-30'), false);
    }
  } finally { if (original === undefined) delete process.env.TZ; else process.env.TZ = original; }
  assert.equal(safeCalendarLink('javascript:alert(1)'), null);
  assert.equal(safeCalendarLink('data:text/html,bad'), null);
  assert.equal(safeCalendarLink('https://maps.example/club'), 'https://maps.example/club');
});

test('rendered member and staff calendars show real content, safe links, unavailable states and cancellation controls', async () => {
  const hooks = registerHooks({ load(url, context, nextLoad) {
    if (url.endsWith('.svelte')) {
      const filename = fileURLToPath(url);
      return { format: 'module', source: compile(readFileSync(filename, 'utf8'), { filename, generate: 'server' }).js.code, shortCircuit: true };
    }
    if (url.endsWith('.css')) return { format: 'module', source: 'export {};', shortCircuit: true };
    return nextLoad(url, context);
  } });
  try {
    const { default: MobileCalendar } = await import('../src/lib/mobile/components/MobileCalendar.svelte');
    const noop = () => false;
    const base = { status: 'ready', error: null, responses: null, responsesKey: null, responsesStatus: 'idle', responsesError: null,
      onLoad: noop, onLoadMore: noop, onRespond: noop, onLoadResponses: noop, onCloseResponses: noop };
    for (const role of ['parent', 'player', 'coach', 'management']) {
      const result = calendar(role, { items: [item({ description: '<script>privateText()</script>', location_url: 'javascript:alert(1)', is_cancelled: true })] });
      const { body } = render(MobileCalendar, { props: { ...base, role, calendar: result } });
      assert.ok(body.includes(`data-calendar-role="${role}"`));
      assert.ok(body.includes('Real training'));
      assert.ok(body.includes('18:30'));
      assert.ok(body.includes('Asia/Nicosia'));
      assert.ok(body.includes('&lt;script>privateText()&lt;/script>'));
      assert.equal(body.includes('<script>privateText()'), false);
      assert.equal(body.includes('href="javascript:'), false);
      if (role === 'parent' || role === 'player') {
        assert.match(body, /<fieldset[^>]*disabled/);
        assert.ok(body.includes('calendar-rsvp-maybe-training:shared-id'));
        assert.equal(body.includes('Απαντήσεις συμμετοχής'), false);
      } else {
        assert.equal(body.includes('calendar-rsvp-'), false);
        assert.ok(body.includes('Απαντήσεις συμμετοχής'));
      }
      assert.equal(body.includes('calendar-player-select'), role === 'parent');
    }
    const unavailable = render(MobileCalendar, { props: { ...base, role: 'parent', status: 'error', error: 'Import unavailable', calendar: null } }).body;
    assert.ok(unavailable.includes('calendar-unavailable'));
    assert.ok(unavailable.includes('Δοκιμή ξανά'));
    assert.equal(unavailable.includes('calendar-empty'), false);
    const empty = render(MobileCalendar, { props: { ...base, role: 'parent', calendar: calendar('parent', { items: [] }) } }).body;
    assert.ok(empty.includes('calendar-empty'));
    assert.equal(empty.includes('calendar-unavailable'), false);
    const unanswered = render(MobileCalendar, { props: { ...base, role: 'coach', calendar: calendar('coach'),
      responsesKey: 'training:shared-id', responsesStatus: 'ready', responses: replies({ players: [{ ...replies().players[0], status: 'no_response' }] }) } }).body;
    assert.ok(unanswered.includes('Scoped player'));
    assert.ok(unanswered.includes('Χωρίς απάντηση'));
  } finally { hooks.deregister(); }
});
