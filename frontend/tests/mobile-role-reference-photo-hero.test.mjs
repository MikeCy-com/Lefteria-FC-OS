import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../src/lib/mobile/components/'+p,import.meta.url),'utf8');
const parent=read('MobileParentHome.svelte');
const roles=read('MobileRoleHome.svelte');

test('parent role retains verified family calendar while gaining a branded photo hero',()=>{
  assert.ok(compile(parent,{filename:'MobileParentHome.svelte',generate:'server'}).js.code.length>100);
  assert.ok(parent.includes('data-testid="parent-photo-hero"'));
  assert.ok(parent.includes('heroUrl'));
  assert.ok(parent.includes('class:parent-hero-photographic={!!heroUrl}'));
  assert.ok(parent.includes('parent-hero-description'));
  assert.ok(parent.includes("matchingParentHomeCalendar(family, calendar, identityId, today, activeChild?.id)"));
  assert.ok(parent.includes('data-testid="parent-home-child-list"'));
  assert.ok(parent.includes('data-testid="parent-home-rsvp-actions"'));
  assert.ok(parent.includes('data-testid="parent-home-shortcuts"'));
  assert.ok(parent.includes('organizationLabel(child.organization_id)'));
  assert.ok(!parent.includes('localStorage'));
});
test('photographic hero is specific to verified player and coach views',()=>{
  assert.ok(compile(roles,{filename:'MobileRoleHome.svelte',generate:'server'}).js.code.length>100);
  assert.ok(roles.includes("class:hero-photographic={!!heroUrl&&(role==='player'||role==='coach')}"));
  assert.ok(roles.includes('data-testid="mobile-role-photo-hero"'));
  assert.ok(roles.includes('heroUrl'));
  assert.ok(!roles.includes('publicImages.hero'));
  assert.ok(roles.includes('data-testid="role-home-matchday"'));
  assert.ok(roles.includes('data-testid="role-home-schedule"'));
  assert.ok(roles.includes('trustedNextRoleMatch(calendar,role,identityId,organizationId,today)'));
  assert.ok(roles.includes("role==='fan'||!contextIsCurrent?null"));
  assert.ok(roles.includes('isTrustedRoleHomeContext(calendar,role,identityId,organizationId,today)'));
  assert.ok(!roles.includes('localStorage'));
});
