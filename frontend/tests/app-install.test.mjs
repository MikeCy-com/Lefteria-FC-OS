import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import { createInstallController, isAppleMobile } from '../src/lib/mobile/app-install.ts';

function browser(options = {}) {
  const events = new EventTarget();
  const display = Object.assign(new EventTarget(), { matches: options.standalone ?? false });
  const registrations = [];
  Object.assign(events, {
    matchMedia: () => display, isSecureContext: options.secure ?? true,
    navigator: { userAgent: '', platform: '', maxTouchPoints: 0, ...options.navigator,
      serviceWorker: { register: async (...args) => { registrations.push(args); } } }
  });
  return { events, display, registrations };
}
function promptEvent(prompt = async () => {}, outcome = 'dismissed') {
  return Object.assign(new Event('beforeinstallprompt', { cancelable: true }), {
    prompt, userChoice: Promise.resolve({ outcome })
  });
}
function observe(controller) {
  let current; controller.subscribe(value => { current = value; });
  return () => current;
}

test('install calls the browser prompt in the original gesture and consumes it once', async () => {
  const controller = createInstallController(), state = observe(controller);
  const { events } = browser(); const stop = controller.start(events);
  let called = 0;
  const event = promptEvent(() => { called++; return Promise.resolve(); });
  events.dispatchEvent(event);
  assert.equal(event.defaultPrevented, true); assert.equal(state().available, true);
  const pending = controller.install();
  assert.equal(called, 1); assert.equal(state().busy, true);
  assert.equal(await controller.install(), 'busy');
  assert.equal(await pending, 'prompted'); assert.equal(state().installed, false);
  assert.equal(await controller.install(), 'help'); assert.equal(called, 1);
  events.dispatchEvent(promptEvent()); assert.equal(state().available, true);
  stop();
});

test('accepted prompt waits for actual installation; appinstalled removes the old prompt', async () => {
  const controller = createInstallController(), state = observe(controller);
  const { events } = browser(); controller.start(events);
  events.dispatchEvent(promptEvent(async () => {}, 'accepted'));
  assert.equal(await controller.install(), 'prompted'); assert.equal(state().installed, false);
  events.dispatchEvent(new Event('appinstalled'));
  assert.equal(state().installed, true); assert.equal(state().available, false);
  assert.equal(await controller.install(), 'installed');
});

test('unsupported and rejected prompts provide guidance without redirecting or retrying', async () => {
  const controller = createInstallController(), state = observe(controller);
  const { events } = browser(); controller.start(events);
  assert.equal(await controller.install(), 'help');
  events.dispatchEvent(promptEvent(async () => { throw new Error('not permitted'); }));
  assert.equal(await controller.install(), 'help'); assert.equal(state().busy, false);
  assert.equal(state().available, false);
});

test('iPhone, iPad desktop UA and standalone modes select the correct install state', () => {
  assert.equal(isAppleMobile('iPhone', 'iPhone', 1), true);
  assert.equal(isAppleMobile('Macintosh', 'MacIntel', 5), true);
  assert.equal(isAppleMobile('Macintosh', 'MacIntel', 0), false);
  assert.equal(isAppleMobile('Android', 'Linux', 5), false);
  const controller = createInstallController(), state = observe(controller);
  const { events, display } = browser({ navigator: { userAgent: 'iPhone', standalone: true } });
  controller.start(events); assert.equal(state().installed, true); assert.equal(state().ios, true);
  events.navigator.standalone = false;
  display.dispatchEvent(new Event('change')); assert.equal(state().installed, false);
  display.matches = true; display.dispatchEvent(new Event('change')); assert.equal(state().installed, true);
});

test('install worker registration preserves push scope and cleanup removes prompt listeners', () => {
  const controller = createInstallController(), state = observe(controller);
  const { events, registrations } = browser(); const stop = controller.start(events);
  assert.deepEqual(registrations, [['/app-service-worker.js', { scope: '/', updateViaCache: 'none' }]]);
  stop(); events.dispatchEvent(promptEvent()); assert.equal(state().available, false);
  const insecure = browser({ secure: false }); controller.start(insecure.events);
  assert.equal(insecure.registrations.length, 0);
});

test('navigation worker never intercepts API requests and returns a noncached generic offline page', async () => {
  let handler, requests = 0, response;
  const source = readFileSync(new URL('../static/app-service-worker.js', import.meta.url), 'utf8');
  vm.runInNewContext(source, {
    self: { addEventListener: (name, fn) => { assert.equal(name, 'fetch'); handler = fn; } },
    fetch: async () => { requests++; throw new Error('offline'); }, Response
  });
  handler({ request: { mode: 'cors', url: 'https://example.test/api/mobile/profile' },
    respondWith: () => assert.fail('API fetch was intercepted') });
  assert.equal(requests, 0);
  handler({ request: { mode: 'navigate' }, respondWith: value => { response = value; } });
  const offline = await response;
  assert.equal(offline.status, 503); assert.equal(offline.headers.get('Cache-Control'), 'no-store');
  assert.match(await offline.text(), /Δεν υπάρχει σύνδεση/);
  assert.doesNotMatch(source, /caches\.|localStorage|sessionStorage|pushManager/);
});

test('installed OS manifest is distinct from original app and has valid PWA icon sizes', () => {
  const manifest = JSON.parse(readFileSync(new URL('../static/manifest.webmanifest', import.meta.url)));
  assert.equal(manifest.start_url, '/app');
  assert.equal(manifest.scope, '/');
  assert.equal(manifest.id, '/lefteria-fc-os');
  assert.equal(manifest.display, 'standalone');
  assert.match(manifest.name, /OS/);
  for (const size of ['192x192','512x512'])
    assert.ok(manifest.icons.some(icon => icon.sizes === size && icon.src.startsWith('/os/')),size);
  const component = readFileSync(new URL('../src/lib/public/AppInstallButton.svelte', import.meta.url), 'utf8');
  assert.match(component, /Εγκατάσταση επίσημης εφαρμογής OS/);
  assert.match(component, /<dialog[^>]*aria-labelledby="app-install-title"/);
  assert.match(component, /Προσθήκη στην οθόνη Αφετηρίας/);
  assert.match(component, /lockPageScroll\\(document\\)/);
  assert.match(component, /void installCurrentSite\\(\\)/);
  assert.doesNotMatch(component, /api\\/app\\/android|official-android-apk|emergentagent/);
  const mobileLogin = readFileSync(new URL('../src/routes/app/login/+page.svelte', import.meta.url), 'utf8');
  const mobileApp = readFileSync(new URL('../src/routes/app/[...path]/+page.svelte', import.meta.url), 'utf8');
  assert.match(mobileLogin,/data-testid="mobile-login-os-install"/);
  assert.match(mobileApp,/data-testid="mobile-more-os-install"/);
});

test('Android uses native OS PWA prompt while iOS and desktop retain their own install flow', () => {
  const controller = createInstallController(), state = observe(controller);
  const android = browser({ navigator:{userAgent:'Mozilla/5.0 (Linux; Android 15)'} });
  const stop = controller.start(android.events);
  assert.equal(state().android, true); assert.equal(state().ios, false);
  stop();
  controller.start(browser({navigator:{userAgent:'iPhone'}}).events);
  assert.equal(state().android, false); assert.equal(state().ios, true);
});
