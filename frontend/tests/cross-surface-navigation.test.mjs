import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL('../src/lib/' + path, import.meta.url), 'utf8');

test('public shop navigation does not promise a ticketing workflow', () => {
  const navigation = read('public/PublicNavigation.svelte');
  assert.match(navigation, /href="\/shop"[^\n]*>ΚΑΤΑΣΤΗΜΑ/);
  assert.doesNotMatch(navigation, /href="\/shop"[^\n]*>ΕΙΣΙΤΗΡΙΑ/);
});

test('admin differentiates public website from authenticated member app', () => {
  const admin = read('admin/AdminFrame.svelte');
  assert.match(admin, /href="\/" aria-label="Προβολή δημόσιας ιστοσελίδας"/);
  assert.match(admin, /href="\/app" aria-label="Άνοιγμα εφαρμογής μελών"/);
  assert.match(admin, /adminSectionsFor\(user\)/);
  assert.match(admin, /data-testid="admin-logout"/);
});

test('mobile role menu can be dismissed by tapping outside without changing identity', () => {
  const shell = read('mobile/components/MobileShell.svelte');
  assert.match(shell, /<svelte:window onpointerdown=\{dismissRoles\}/);
  assert.match(shell, /roleMenu\?\.contains\(target\) \|\| roleTrigger\?\.contains\(target\)/);
  assert.match(shell, /roleMenuOpen = false;/);
  assert.match(shell, /onRoleChange\(option\)/);
});