import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

// This tests the actual Svelte server rendering, not a detached HTML mock.
registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier === '$app/state') return { url: 'data:text/javascript,' + encodeURIComponent('export const page={url:new URL("https://fixture.test/academy/philosophy")};'), shortCircuit: true };
    if (specifier.startsWith('.') && /\/src\//.test(context.parentURL ?? '') &&
        !/\.[a-z]+$/i.test(specifier)) {
      const candidate = new URL(specifier + '.ts', context.parentURL);
      if (existsSync(candidate)) return nextResolve(candidate.href, context);
    }
    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (url.endsWith('.css')) return { format: 'module', source: 'export {};', shortCircuit: true };
    if (url.endsWith('.svelte')) {
      const filename = fileURLToPath(url);
      return { format: 'module',
        source: compile(readFileSync(filename, 'utf8'), { filename, generate: 'server' }).js.code,
        shortCircuit: true };
    }
    return nextLoad(url, context);
  }
});

test('Academy philosophy reuses the public shell, scoped organization and original value order', async () => {
  const { default: Philosophy } = await import('../src/routes/academy/philosophy/+page.svelte');
  const site = { organization_id: 'lefteria_fc_academy', club: null, academy_groups: [], teams: [], fixtures: [], news: [] };
  const html = render(Philosophy, {
    props: { data: { site, branding: null, organization: 'lefteria_fc_academy',
      available: true, navigationGroups: [] } }
  }).body;

  assert.match(html, /data-testid="public-site"/);
  assert.match(html, /data-organization="lefteria_fc_academy"/);
  assert.match(html, /data-testid="philosophy-page"/);
  assert.match(html, /data-testid="approach-section"/);
  assert.match(html, /Η ΦΙΛΟΣΟΦΙΑ/);

  const expected = ['ΠΑΘΟΣ', 'ΠΕΙΘΑΡΧΙΑ', 'ΟΜΑΔΙΚΟΤΗΤΑ', 'ΑΝΑΠΤΥΞΗ', 'ΑΡΙΣΤΕΙΑ', 'ΚΑΙΝΟΤΟΜΙΑ'];
  for (let i = 0; i < expected.length; i++) {
    assert.match(html, new RegExp('data-testid="value-' + i + '"'));
    assert.ok(html.indexOf(expected[i]) >= 0, expected[i]);
    if (i > 0) assert.ok(html.indexOf(expected[i - 1]) < html.indexOf(expected[i]), 'original value ordering');
  }
  for (const title of ['Εξατομικευμενη Αναπτυξη', 'Δομημενο Προγραμμα',
    'Ασφαλες Περιβαλλον', 'Συνεργασια με Γονεις']) {
    assert.ok(html.includes(title), title);
  }
  assert.equal((html.match(/data-testid="value-[0-5]"/g) ?? []).length, 6);
  assert.doesNotMatch(html, /medical_notes|coach_phone|private_contact|Bearer/);
});

test('Academy philosophy server loader fixes the public organization scope', async () => {
  const loader = readFileSync(new URL('../src/routes/academy/philosophy/+page.server.ts', import.meta.url), 'utf8');
  assert.match(loader, /loadPublicSite\(url, 'lefteria_fc_academy'\)/);
  assert.doesNotMatch(loader, /Authorization|Cookie|headers/);
});
