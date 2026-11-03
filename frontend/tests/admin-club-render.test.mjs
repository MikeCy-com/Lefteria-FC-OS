import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createClubEditorController } from '../src/lib/admin/club-controller.ts';

test('actual club form renders safe public fields, scoped choices, read-only permissions and explicit conflict recovery', async () => {
  const hooks = registerHooks({ load(url, context, nextLoad) {
    if (url.endsWith('.svelte')) { const filename = fileURLToPath(url); return { format: 'module',
      source: compile(readFileSync(filename, 'utf8'), { filename, generate: 'server' }).js.code, shortCircuit: true }; }
    return nextLoad(url, context);
  } });
  try {
    const { default: Editor } = await import('../src/lib/admin/ClubEditor.svelte');
    const user = { id: 'fixture', username: 'fixture', name: 'Fixture', role: 'manager', permissions: ['settings:read'],
      organizations: [{ id: 'lefteria_fc_academy', name: 'Fixture Academy' }] };
    const data = { version: 5, club: { id: 'fixture-club', organization_id: 'lefteria_fc_academy', name: 'Recorded Academy',
      social_links: {}, founded: null, history: '<script>PRIVATE-FIXTURE</script>' } };
    const controller = createClubEditorController({ baseUrl: 'https://fixture.invalid', onUnauthenticated() {},
      fetch: async request => request.method === 'GET' ? Response.json(data) : Response.json({}, { status: 409 }) });
    await controller.setAuthority(user);
    let html = render(Editor, { props: { controller } }).body;
    assert.match(html, /Recorded Academy/); assert.match(html, /Fixture Academy/);
    assert.doesNotMatch(html, /<option[^>]+value="lefteria_fc"/);
    assert.match(html, /readonly/); assert.match(html, /maxlength="100000"/);
    assert.doesNotMatch(html, /data-testid="club-edit"|data-testid="club-save"|bank_iban|twilio|password|<script>PRIVATE/);
    await controller.setAuthority({ ...user, permissions: ['settings:read', 'settings:write'] });
    controller.edit(); controller.change('name', 'Changed Academy'); await controller.save();
    html = render(Editor, { props: { controller } }).body;
    assert.match(html, /club-reload-conflict/); assert.match(html, /Changed Academy/);
    assert.match(html, /data-testid="club-save"[^>]*disabled|disabled[^>]*data-testid="club-save"/);
    assert.doesNotMatch(html, /data-testid="club-saved"/);
    const { default: Workspace } = await import('../src/lib/admin/AdminWorkspace.svelte');
    const session = { user, expires_at: '2099-01-01T00:00:00Z' };
    const workspace = render(Workspace, { props: { session, onLogout() {}, onRefresh() {} } }).body;
    assert.match(workspace, /href="\/admin\/club"/);
    for (const patch of [{ permissions: ['news:*'] }, { organizations: [] }]) {
      const hidden = render(Workspace, { props: { session: { ...session, user: { ...user, ...patch } }, onLogout() {}, onRefresh() {} } }).body;
      assert.doesNotMatch(hidden, /href="\/admin\/club"/);
    }
    controller.dispose();
  } finally { hooks.deregister(); }
});
