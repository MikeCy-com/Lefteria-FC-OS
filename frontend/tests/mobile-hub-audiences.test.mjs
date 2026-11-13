import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { allowedTabs, normalizeTab, primaryTabs } from '../src/lib/mobile/navigation.ts';

const read = (relative) => readFileSync(new URL('../src/' + relative, import.meta.url), 'utf8');

test('Hub entry is available to parent, player and fan without becoming a role switch', () => {
  for (const role of ['parent', 'player', 'fan']) {
    assert.ok(allowedTabs(role).includes('hub'), role);
    assert.equal(normalizeTab('hub', role), 'hub', role);
  }
  for (const role of ['coach', 'management']) {
    assert.ok(!allowedTabs(role).includes('hub'), role);
    assert.equal(normalizeTab('hub', role), 'home', role);
  }
  assert.equal(normalizeTab('education', 'parent'), 'education');
  assert.ok(!primaryTabs('fan').includes('hub'));
});

test('Hub is only a preview for fan and never claims subscription or authorization', () => {
  const component = read('lib/mobile/components/MobileAcademyEducation.svelte');
  const route = read('routes/app/[...path]/+page.svelte');
  assert.match(component, /viewerRole === 'fan'/);
  assert.match(component, /data-testid="hub-fan-preview"/);
  assert.match(component, /δεν δημιουργεί συνδρομή/);
  assert.match(component, /δεν αλλάζει τον ρόλο/);
  assert.match(component, /viewerRole === 'fan'\s*\? \(fanInterest === 'player' \? 'player' : 'parent'\) : viewerRole/);
  assert.match(component, /fanInterest === 'both'/);
  assert.match(component, /if \(viewerRole === 'fan'\) return \[\]/);
  assert.match(component, /viewerRole === 'parent'/);
  assert.match(component, /viewerRole === 'player'/);
  assert.match(route, /MobileAcademyEducation viewerRole=\{role\}/);
  assert.doesNotMatch(component, /fetch\(|localStorage|POST|payment|payment_method|player_id/);
});

test('Hub continues to use only the first-party reviewed draft education collection', () => {
  const component = read('lib/mobile/components/MobileAcademyEducation.svelte');
  assert.match(component, /searchAcademyEducation\(stage,audience,topic,search\)/);
  assert.match(component, /Δεν αποτελεί ακόμη επίσημη δημοσίευση/);
  assert.doesNotMatch(component, /<iframe|{@html|innerHTML|createObjectURL/);
});
