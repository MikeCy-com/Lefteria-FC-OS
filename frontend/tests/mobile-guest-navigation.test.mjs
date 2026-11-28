import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync } from 'node:fs';
import { GUEST_PUBLIC_LINKS, guestRequestedTab, guestPublicLink, guestLoginHref, isGuestPublicTab, mobileAfterLogin, roleCanResumeTab } from '../src/lib/mobile/guest-navigation.ts';
import { allowedTabs, TAB_NAMES } from '../src/lib/mobile/navigation.ts';

test('guest starts in public fan home and can browse only allowlisted public destinations', () => {
  assert.equal(guestRequestedTab({ pathname: '/app', search: '' }), 'home');
  assert.equal(guestRequestedTab({ pathname: '/app/news', search: '' }), 'news');
  assert.equal(guestRequestedTab({ pathname: '/app', search: '?tab=fixtures' }), 'fixtures');
  const publicTabs = ['home', 'more', 'news', 'matches', 'fixtures', 'standings', 'team', 'shop', 'potm'];
  for (const tab of Object.keys(TAB_NAMES)) assert.equal(isGuestPublicTab(tab), publicTabs.includes(tab), tab);
  for (const link of GUEST_PUBLIC_LINKS) {
    assert.ok(link.href.startsWith('/') && !link.href.startsWith('//'));
    assert.ok(!link.href.startsWith('/api/'));
    assert.equal(guestPublicLink(link.tab), link);
  }
  assert.equal(guestPublicLink('profile'), null);
  assert.equal(guestPublicLink('calendar'), null);
});

test('direct protected app destinations retain only the recognized tab', () => {
  for (const [path, search, expected] of [
    ['/app/calendar', '', 'calendar'],
    ['/app/family', '', 'children'],
    ['/app', '?tab=financials&player_id=private-id', 'financials'],
    ['/app', '?tab=education_hub', 'hub'],
    ['/app', '?tab=profile&role=management', 'profile'],
    ['/app', '?tab=__proto__', 'home'],
    ['/app', '?tab=constructor', 'home'],
    ['/app', '?tab=https%3A%2F%2Fexample.test', 'home']
  ]) {
    const tab = guestRequestedTab({pathname:path,search});
    assert.equal(tab, expected);
    assert.equal(guestLoginHref(tab), '/app/login?return_tab='+expected);
  }
});

test('post-login return respects selected current role, never cached or caller-supplied grants', () => {
  for (const role of ['fan', 'parent', 'player', 'coach', 'management']) {
    for (const tab of Object.keys(TAB_NAMES)) {
      const expected = tab !== 'home' && allowedTabs(role).includes(tab) ? '/app?tab='+tab : '/app';
      assert.equal(mobileAfterLogin(tab, role, true), expected, role+':'+tab);
      assert.equal(mobileAfterLogin(tab, role, false), '/app', role+':'+tab+' revoked');
    }
  }
  for (const value of [null, {}, '__proto__', 'constructor', '//evil.test', 'https://evil.test', '/admin', '/api/mobile/profile', 'profile&role=management', 'profile#x', 'profile%26role=management']) {
    assert.equal(mobileAfterLogin(value, 'fan', true), '/app');
  }
  assert.equal(mobileAfterLogin('financials', 'fan', true), '/app');
  assert.equal(mobileAfterLogin('financials', 'super_admin', true), '/app');
  assert.equal(mobileAfterLogin('profile', 'fan', true), '/app?tab=profile');
});

test('guest shell is distinct from verified member UI and cannot request mobile APIs', () => {
  const read = path => readFileSync(new URL('../src/'+path, import.meta.url), 'utf8');
  const guest = read('lib/mobile/components/MobileGuestHome.svelte');
  const app = read('routes/app/[...path]/+page.svelte');
  const shell = read('lib/mobile/components/MobileShell.svelte');
  assert.doesNotMatch(guest, /\/api\/mobile\/|MobileAuthController|MobileProfile|identityId=|MobileParentHome/);
  assert.match(guest, /<MobileShell guest role="fan"/);
  assert.match(guest, /availableRoles=\{\[\]\}/);
  assert.ok(app.includes("$controller.status === 'authenticated' && user && role"));
  assert.ok(app.includes("$controller.status === 'anonymous' && isGuestPublicTab(guestTab)"));
  assert.ok(app.includes('guestLoginHref(guestTab)'));
  assert.ok(shell.includes('!guest && canComposePost(role)'));
  assert.ok(shell.includes('guest ? "mobile-guest-app" : "mobile-app"'));
});

test('role picker compatibility uses only valid requested tabs and existing mobile roles', () => {
  for (const role of ['fan','parent','player','coach','management']) {
    for (const tab of Object.keys(TAB_NAMES)) assert.equal(roleCanResumeTab(tab, role), allowedTabs(role).includes(tab));
  }
  assert.equal(roleCanResumeTab('financials','fan'), false);
  assert.equal(roleCanResumeTab('financials','parent'), true);
  assert.equal(roleCanResumeTab('squad','coach'), true);
  assert.equal(roleCanResumeTab('profile','super_admin'), false);
  assert.equal(roleCanResumeTab('https://evil.test','fan'), false);
  assert.equal(roleCanResumeTab(null,'fan'), false);
});
