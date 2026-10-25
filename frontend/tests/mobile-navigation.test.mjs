import test from 'node:test';
import assert from 'node:assert/strict';
import {
  MOBILE_ROLE_DEFINITIONS, ROLE_NAMES, TAB_NAMES, allowedTabs, bottomTabFor,
  canComposePost, isMobileRole, normalizeTab, primaryTabs, tabFromLocation
} from '../src/lib/mobile/navigation.ts';

const roles = ['fan', 'player', 'parent', 'coach', 'management'];

test('only the five source mobile identities are accepted as roles', () => {
  for (const role of roles) assert.equal(isMobileRole(role), true);
  for (const value of ['admin', 'super_admin', 'toString', '__proto__', '', null, undefined, 1]) {
    assert.equal(isMobileRole(value), false);
  }
});

test('role and tab labels retain the Greek source navigation contract', () => {
  assert.equal(ROLE_NAMES.parent, 'Γονέας');
  assert.equal(ROLE_NAMES.coach, 'Προπονητής');
  assert.equal(ROLE_NAMES.management, 'Διοίκηση');
  assert.equal(TAB_NAMES.calendar, 'Πρόγραμμα');
  assert.equal(TAB_NAMES.team, 'Ομάδα & Ακαδημία');
  assert.equal(TAB_NAMES.social, 'Lefteria');
});

test('primary tabs preserve the role-specific source order', () => {
  assert.deepEqual(primaryTabs('fan'), ['home', 'matches', 'gamehub', 'shop']);
  assert.deepEqual(primaryTabs('player'), ['home', 'matches', 'social', 'shop']);
  assert.deepEqual(primaryTabs('parent'), ['home', 'calendar', 'children', 'chat', 'more']);
  assert.deepEqual(primaryTabs('coach'), ['home', 'calendar', 'squad', 'chat', 'more']);
  assert.deepEqual(primaryTabs('management'), ['home', 'calendar', 'teams', 'financials', 'more']);
});

test('every role starts at home and each primary destination is allowed', () => {
  for (const role of roles) {
    assert.equal(MOBILE_ROLE_DEFINITIONS[role].defaultTab, 'home');
    assert.equal(new Set(allowedTabs(role)).size, allowedTabs(role).length);
    for (const tab of primaryTabs(role)) assert.ok(allowedTabs(role).includes(tab), `${role}: ${tab}`);
    for (const tab of ['home', 'profile', 'notifications', 'more']) assert.equal(normalizeTab(tab, role), tab);
  }
});

test('fan destinations retain the public football, game and shop menu', () => {
  for (const tab of ['matches', 'gamehub', 'news', 'shop', 'team', 'fixtures', 'standings', 'predict', 'potm']) {
    assert.equal(normalizeTab(tab, 'fan'), tab);
  }
  for (const tab of ['calendar', 'chat', 'announcements', 'children', 'squad', 'teams', 'financials']) {
    assert.equal(normalizeTab(tab, 'fan'), 'home');
  }
});

test('member destinations retain calendar, matches, chat and social tabs', () => {
  for (const role of ['player', 'parent', 'coach', 'management']) {
    for (const tab of ['calendar', 'matches', 'chat', 'announcements', 'social']) assert.equal(normalizeTab(tab, role), tab);
    for (const tab of ['gamehub', 'team', 'fixtures', 'standings', 'predict', 'potm']) assert.equal(normalizeTab(tab, role), 'home');
  }
});

test('role-specific destinations stay within the selected role', () => {
  assert.equal(normalizeTab('children', 'parent'), 'children');
  assert.equal(normalizeTab('financials', 'parent'), 'financials');
  assert.equal(normalizeTab('squad', 'coach'), 'squad');
  assert.equal(normalizeTab('teams', 'management'), 'teams');
  assert.equal(normalizeTab('financials', 'management'), 'financials');
  assert.equal(normalizeTab('shop', 'player'), 'shop');
  assert.equal(normalizeTab('shop', 'parent'), 'home');
  assert.equal(normalizeTab('children', 'coach'), 'home');
  assert.equal(normalizeTab('squad', 'management'), 'home');
  assert.equal(normalizeTab('financials', 'player'), 'home');
});

test('schedule, training and event deep links resolve to the member calendar', () => {
  for (const alias of ['schedule', 'training', 'event']) {
    for (const role of ['player', 'parent', 'coach', 'management']) {
      assert.equal(normalizeTab(alias, role), 'calendar');
      assert.equal(tabFromLocation({ pathname: `/app/${alias}`, search: '' }, role), 'calendar');
    }
    assert.equal(normalizeTab(alias, 'fan'), 'home');
  }
});

test('legacy match and role aliases retain their source destinations', () => {
  for (const role of roles) {
    assert.equal(normalizeTab('match', role), 'matches');
    for (const alias of ['parent', 'player', 'coach', 'management']) assert.equal(normalizeTab(alias, role), 'home');
  }
});

test('fan social and reels aliases go home while member social stays available', () => {
  assert.equal(normalizeTab('social', 'fan'), 'home');
  assert.equal(normalizeTab('reels', 'fan'), 'home');
  assert.equal(tabFromLocation({ pathname: '/app/social', search: '?tab=reels' }, 'fan'), 'home');
  for (const role of ['player', 'parent', 'coach', 'management']) {
    assert.equal(normalizeTab('social', role), 'social');
    assert.equal(normalizeTab('reels', role), 'home');
  }
});

test('query takes precedence over path and decodes aliases', () => {
  assert.equal(tabFromLocation({ pathname: '/app/calendar', search: '?tab=chat' }, 'parent'), 'chat');
  assert.equal(tabFromLocation({ pathname: '/app/matches', search: '?tab=%73chedule' }, 'coach'), 'calendar');
  assert.equal(tabFromLocation({ pathname: '/app/matches', search: '?tab=&other=1' }, 'parent'), 'matches');
  assert.equal(tabFromLocation({ pathname: '/app/calendar', search: '?tab=missing' }, 'parent'), 'home');
  assert.equal(tabFromLocation({ pathname: '/app', search: '?tab=shop&tab=home' }, 'fan'), 'shop');
});

test('empty and unsupported inputs fall back without changing legacy URLs', () => {
  for (const role of roles) {
    for (const tab of [undefined, null, '', 'missing', 'HOME', ' home ', '<script>']) assert.equal(normalizeTab(tab, role), 'home');
    assert.equal(tabFromLocation({ pathname: '/app', search: '' }, role), 'home');
    assert.equal(tabFromLocation({ pathname: '/app/unknown', search: '' }, role), 'home');
  }
});

test('only fan/player expose the composer; parent mode has no plus action', () => {
  assert.equal(canComposePost('fan'), true);
  assert.equal(canComposePost('player'), true);
  assert.equal(canComposePost('parent'), false);
  assert.equal(canComposePost('coach'), false);
  assert.equal(canComposePost('management'), false);
});

test('header and nested destinations do not select another bottom tab', () => {
  assert.equal(bottomTabFor('calendar', 'parent'), 'calendar');
  assert.equal(bottomTabFor('social', 'player'), 'social');
  assert.equal(bottomTabFor('financials', 'management'), 'financials');
  assert.equal(bottomTabFor('financials', 'parent'), null);
  for (const role of roles) {
    assert.equal(bottomTabFor('profile', role), null);
    assert.equal(bottomTabFor('notifications', role), null);
    assert.equal(bottomTabFor('schedule', role), null);
  }
});
