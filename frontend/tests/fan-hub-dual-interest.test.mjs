import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {recommendStarterEducation} from '../src/lib/mobile/education-recommendations.ts';
import {canAccessSubscribedHub} from '../src/lib/hub/hub-policy.ts';

const source=readFileSync(new URL('../src/lib/mobile/components/MobileAcademyEducation.svelte',import.meta.url),'utf8');
test('fans may choose parent-interest, player-interest or both without changing role',()=>{
  assert.match(source,/let fanInterest = \$state<EducationAudience \| 'both'>\('parent'\)/);
  assert.match(source,/fanInterest = 'parent'/);
  assert.match(source,/fanInterest = 'player'/);
  assert.match(source,/fanInterest = 'both'/);
  assert.match(source,/Και τα δύο/);
  assert.match(source,/Επιλεγμένο ενδιαφέρον:/);
  assert.match(source,/if \(viewerRole === 'fan'\) return \[\]/);
  assert.match(source,/data-testid="hub-subscription-required"/);
  assert.doesNotMatch(source,/checkout\(|paymentUrl|automaticCharge|fetch\(/);
});
test('interest-only fan choice never creates permission or access',()=>{
  assert.deepEqual(recommendStarterEducation({role:'fan',stage:'10-12',limit:10}),[]);
  assert.equal(canAccessSubscribedHub('fan1','fan',null,'parent','2026-11-13T10:00:00Z'),false);
  assert.equal(canAccessSubscribedHub('fan1','fan',null,'player','2026-11-13T10:00:00Z'),false);
});
