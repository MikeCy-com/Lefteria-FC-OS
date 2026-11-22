import test from 'node:test';
import assert from 'node:assert/strict';
import {
  MOBILE_ROLE_DEFINITIONS, TAB_NAMES, allowedTabs, primaryTabs,
  tabFromLocation, contextualBottomTabFor, fanPublicDestination
} from '../src/lib/mobile/navigation.ts';

// Navigation is a UI affordance, not a substitute for API authorization.
// Exhaust every declared tab against every role to prevent future additions
// from accidentally inheriting a foreign role's bottom navigation.
test('all declared deep links remain inside the active mobile role', () => {
  for (const role of Object.keys(MOBILE_ROLE_DEFINITIONS)) {
    const allowed = new Set(allowedTabs(role));
    const primary = new Set(primaryTabs(role));
    for (const tab of Object.keys(TAB_NAMES)) {
      for (const location of [
        { pathname: `/app/${tab}`, search: '' },
        { pathname: '/app/home', search: `?tab=${encodeURIComponent(tab)}` }
      ]) {
        const resolved = tabFromLocation(location, role);
        assert.ok(allowed.has(resolved), `${role} exposed ${resolved} via ${tab}`);
        assert.equal(resolved, allowed.has(tab) ? tab : 'home', `${role} resolved ${tab}`);
        const highlighted = contextualBottomTabFor(resolved, role);
        assert.ok(highlighted === null || primary.has(highlighted), `${role} highlighted ${highlighted}`);
        if (role !== 'fan') assert.equal(fanPublicDestination(resolved, role), null);
      }
    }
  }
});

test('malformed or prototype-like tab queries never select foreign navigation', () => {
  for (const role of Object.keys(MOBILE_ROLE_DEFINITIONS)) {
    for (const value of ['__proto__', 'constructor', 'toString', 'financials%00', '%2Fadmin', 'unknown']) {
      const resolved = tabFromLocation({ pathname: '/app/calendar', search: `?tab=${value}` }, role);
      assert.equal(resolved, 'home', `${role}: ${value}`);
      assert.equal(contextualBottomTabFor(resolved, role), 'home');
    }
  }
});
