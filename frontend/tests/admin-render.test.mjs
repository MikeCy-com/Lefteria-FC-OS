import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { hasAdminPermission, isAdminRole, pendingAdminModules } from '../src/lib/admin/permissions.ts';

test('CMS presentation preserves assigned capabilities without inventing role or organization grants', () => {
  assert.equal(isAdminRole('management'), false);
  assert.equal(isAdminRole('toString'), false);
  assert.equal(hasAdminPermission(['training:read_assigned'], 'training:read'), false);
  assert.equal(hasAdminPermission(['news:*'], 'news:write'), true);
  assert.equal(hasAdminPermission(['marketing:read'], 'marketing:write'), false);
  assert.deepEqual(pendingAdminModules(['teams:read_assigned'], []), []);
  assert.deepEqual(pendingAdminModules(['academy:read'], ['lefteria_fc']), []);
  assert.deepEqual(pendingAdminModules(['social:write'], []), ['Social Control Centre']);
});

test('rendered CMS login and session panels expose no token, signup or simulated operational data', async () => {
  const hooks = registerHooks({ load(url, context, nextLoad) {
    if (url.endsWith('.svelte')) {
      const filename = fileURLToPath(url);
      const { js } = compile(readFileSync(filename, 'utf8'), { filename, generate: 'server' });
      return { format: 'module', source: js.code, shortCircuit: true };
    }
    return nextLoad(url, context);
  } });
  try {
    const { default: SignIn } = await import('../src/lib/admin/AdminSignIn.svelte');
    const login = render(SignIn, { props: { onLogin: async () => false } }).body;
    assert.match(login, /autocomplete="username"/);
    assert.match(login, /autocomplete="current-password"/);
    assert.match(login, /type="password"/);
    assert.match(login, /30 ημέρες/);
    assert.match(login, /60 ημέρες/);
    assert.match(login, /90 ημέρες/);
    assert.doesNotMatch(login, /signup|register|invite|token/i);
    const { default: Workspace } = await import('../src/lib/admin/AdminWorkspace.svelte');
    const workspace = render(Workspace, { props: { session: {
      user: { id: '11111111-1111-4111-8111-111111111111', username: 'cms-fixture', name: '<script>fixture</script>', role: 'editor',
        permissions: ['news:*', 'gallery:*'], organizations: [{ id: 'lefteria_fc_academy', name: 'Lefteria FC Academy' }] },
      expires_at: '2026-11-01T20:00:00Z' }, onLogout() {}, onRefresh() {} } }).body;
    assert.match(workspace, /Lefteria FC Academy/);
    assert.match(workspace, /Editor/);
    assert.match(workspace, /Νέα — διαχείριση/);
    assert.doesNotMatch(workspace, /news:\*/);
    assert.match(workspace, /Under Developments/);
    assert.doesNotMatch(workspace, /<script>fixture<\/script>/);
    assert.doesNotMatch(workspace, /Οικονομικά|Διαχείριση λογαριασμών|Όνομα χρήστη.*token/);
  } finally { hooks.deregister(); }
});
