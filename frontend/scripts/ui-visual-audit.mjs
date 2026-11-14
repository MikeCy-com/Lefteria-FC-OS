// Isolated, data-free visual smoke for the public and login experiences.
// This uses only an ephemeral loopback Vite server and never targets Hostinger.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { chromium } from 'playwright';

const frontend = fileURLToPath(new URL('../', import.meta.url));
const artifacts = process.env.LEFTERIA_UI_ARTIFACTS || await mkdtemp(path.join(os.tmpdir(), 'lefteria-ui-'));
const routes = [
  { name:'home', path:'/', expectedStatus:200 },
  { name:'first-team', path:'/team', expectedStatus:200 },
  { name:'team-schedule', path:'/team?tab=schedule', expectedStatus:200 },
  { name:'team-roster-unavailable', path:'/team?tab=roster', expectedStatus:503 },
  { name:'academy', path:'/academy', expectedStatus:200 },
  { name:'academy-philosophy', path:'/academy/philosophy', expectedStatus:200 },
  { name:'about', path:'/about', expectedStatus:200 },
  { name:'contact', path:'/contact', expectedStatus:200 },
  { name:'privacy', path:'/privacy', expectedStatus:200 },
  { name:'web-app-access', path:'/login', expectedStatus:200 },
  { name:'app-login', path:'/app/login', expectedStatus:200 }
];
const viewports = [
  { name:'phone', width:390, height:844 },
  { name:'tablet', width:768, height:1024 },
  { name:'desktop', width:1440, height:900 },
  { name:'4k', width:3840, height:2160 }
];

async function verifyAppInstall(browser, base, artifacts) {
  const checks = [];
  const context = await browser.newContext({ viewport:{width:390,height:844}, reducedMotion:'reduce', locale:'el-GR' });
  try {
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(base + '/', { waitUntil:'networkidle' });
    // Registration is performed by the root onMount: wait for hydration, not a timer.
    await page.waitForFunction(async () => !!await navigator.serviceWorker.getRegistration('/'));
    const install = page.getByTestId('official-app-install');
    await install.waitFor();
    await page.evaluate(() => {
      document.body.style.setProperty('overflow', 'auto', 'important');
      document.body.style.setProperty('padding-right', '7px');
    });
    const original = await page.evaluate(() => ({
      overflow:document.body.style.getPropertyValue('overflow'),
      priority:document.body.style.getPropertyPriority('overflow'),
      padding:document.body.style.getPropertyValue('padding-right')
    }));
    await install.click();
    const dialog = page.getByRole('dialog', { name:'Η επίσημη εφαρμογή Λευτεριά FC' });
    await dialog.waitFor({state:'visible'});
    assert.equal(await dialog.getByRole('link', { name:'Συνέχεια στην εφαρμογή →' }).getAttribute('href'), '/app');
    assert.equal(await page.evaluate(() => document.body.style.overflow), 'hidden');
    await page.screenshot({path:path.join(artifacts,'app-install-help-phone.png'),fullPage:true});
    await page.keyboard.press('Escape');
    await dialog.waitFor({state:'hidden'});
    await page.waitForFunction(() => document.body.style.overflow === 'auto');
    assert.deepEqual(await page.evaluate(() => ({
      overflow:document.body.style.getPropertyValue('overflow'),
      priority:document.body.style.getPropertyPriority('overflow'),
      padding:document.body.style.getPropertyValue('padding-right')
    })), original);
    assert.equal(await install.evaluate(element => document.activeElement === element), true);
    checks.push('unsupported browser guidance, app link, Escape, focus and scroll restoration');

    await install.click();
    await dialog.getByRole('button', { name:'Κλείσιμο οδηγιών εγκατάστασης' }).click();
    await dialog.waitFor({state:'hidden'});
    await page.waitForFunction(() => document.body.style.overflow === 'auto');
    assert.equal(await install.evaluate(element => document.activeElement === element), true);
    checks.push('explicit close restores focus and scroll');

    const prevented = await page.evaluate(() => {
      window.installPromptCalls = 0;
      const event = new Event('beforeinstallprompt', {cancelable:true});
      event.prompt = () => { window.installPromptCalls++; return Promise.resolve(); };
      event.userChoice = Promise.resolve({outcome:'dismissed'});
      window.dispatchEvent(event);
      return event.defaultPrevented;
    });
    assert.equal(prevented, true);
    await install.click();
    await page.waitForFunction(() => !document.querySelector('[data-testid="official-app-install"]').disabled);
    assert.equal(await page.evaluate(() => window.installPromptCalls), 1);
    assert.equal(await dialog.isVisible(), false);
    await install.click();
    await dialog.waitFor({state:'visible'});
    assert.equal(await page.evaluate(() => window.installPromptCalls), 1);
    await page.keyboard.press('Escape');
    checks.push('single-use native prompt from button gesture with dismissed fallback');

    await page.evaluate(() => window.dispatchEvent(new Event('appinstalled')));
    const opened = page.getByTestId('official-app-open');
    await opened.waitFor();
    assert.equal(await opened.getAttribute('href'), '/app');
    assert.equal(await install.count(), 0);
    checks.push('installed state opens the existing app');

    const manifestResponse = await context.request.get(base + '/manifest.webmanifest');
    assert.equal(manifestResponse.status(), 200);
    assert.match(manifestResponse.headers()['content-type'] ?? '', /application\/(?:manifest\+json|json)/);
    const manifest = await manifestResponse.json();
    assert.equal(manifest.start_url, '/app'); assert.equal(manifest.scope, '/');
    const worker = await context.request.get(base + '/app-service-worker.js');
    assert.equal(worker.status(), 200);
    assert.match(worker.headers()['content-type'] ?? '', /(?:text|application)\/javascript/);
    const icon = manifest.icons.find(item => item.sizes === 'any');
    assert.ok(icon);
    const iconResponse = await context.request.get(base + icon.src);
    assert.equal(iconResponse.status(), 200);
    assert.match(iconResponse.headers()['content-type'] ?? '', /image\/svg\+xml/);
    assert.equal(await page.evaluate(async src => {
      const image = new Image(); image.src = src; await image.decode();
      return image.naturalWidth >= 512 && image.naturalHeight >= 512;
    }, icon.src), true);
    checks.push('manifest, worker MIME types and decoded installation icon');
    assert.deepEqual(errors, [], 'Install page browser errors');
  } finally { await context.close(); }

  const apple = await browser.newContext({
    viewport:{width:390,height:844},
    userAgent:'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1',
    isMobile:true, hasTouch:true, reducedMotion:'reduce'
  });
  try {
    const page = await apple.newPage();
    await page.goto(base + '/', { waitUntil:'networkidle' });
    await page.waitForFunction(async () => !!await navigator.serviceWorker.getRegistration('/'));
    await page.getByTestId('official-app-install').click();
    const dialog = page.getByRole('dialog', {name:'Η επίσημη εφαρμογή Λευτεριά FC'});
    await dialog.waitFor({state:'visible'});
    assert.match(await dialog.innerText(), /Safari/);
    assert.match(await dialog.innerText(), /Προσθήκη στην οθόνη Αφετηρίας/);
    await page.screenshot({path:path.join(artifacts,'app-install-ios-guidance.png'),fullPage:true});
    checks.push('iPhone browser guidance without offering an Android APK');
  } finally { await apple.close(); }
  return checks;
}

let server;
let browser;
let ownArtifacts = !process.env.LEFTERIA_UI_ARTIFACTS;
let failures = [];
try {
  await mkdir(artifacts, { recursive:true });
  const probe = createServer();
  probe.listen(0, '127.0.0.1');
  await once(probe, 'listening');
  const port = probe.address().port;
  await new Promise((resolve, reject) => probe.close(error => error ? reject(error) : resolve()));
  const base = 'http://127.0.0.1:' + port;
  server = spawn(process.execPath, ['node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', String(port), '--strictPort'], {
    cwd:frontend, stdio:['ignore','pipe','pipe'], windowsHide:true,
    env:{...process.env, INTERNAL_API_ORIGIN:'http://127.0.0.1:1', HOST:'127.0.0.1'}
  });
  let serverLogs = '';
  for (const stream of [server.stdout, server.stderr]) stream.on('data', chunk => {
    serverLogs = (serverLogs + chunk.toString()).slice(-4000);
  });
  let ready = false;
  for (let attempt = 0; attempt < 120; attempt++) {
    assert.equal(server.exitCode, null, 'Dev server exited: ' + serverLogs);
    try {
      const response = await fetch(base + '/app/login', { signal:AbortSignal.timeout(1500) });
      if (response.ok) { ready = true; break; }
    } catch { /* Starting */ }
    await delay(250);
  }
  assert.ok(ready, 'UI server did not start: ' + serverLogs);
  browser = await chromium.launch({headless:true});
  const results = [];
  for (const viewport of viewports) {
    const context = await browser.newContext({
      viewport:{width:viewport.width,height:viewport.height},
      deviceScaleFactor:1,
      reducedMotion:'reduce',
      locale:'el-GR',
      colorScheme:'dark'
    });
    for (const route of routes) {
      const page = await context.newPage();
      const url = base + route.path;
      const response = await page.goto(url, { waitUntil:'domcontentloaded', timeout:30000 });
      await page.locator('body').waitFor();
      await page.evaluate(() => document.fonts.ready);
      const metrics = await page.evaluate(() => ({
        scrollWidth:document.documentElement.scrollWidth,
        clientWidth:document.documentElement.clientWidth,
        title:document.title,
        main:document.querySelectorAll('main').length
      }));
      const screenshot = `${route.name}-${viewport.name}.png`;
      await page.screenshot({path:path.join(artifacts,screenshot),fullPage:true,animations:'disabled',timeout:30000});
      const status = response?.status() ?? 0;
      results.push({route:route.path,viewport:viewport.name,width:viewport.width,status,...metrics,screenshot});
      // Status assertions distinguish real rendering from a redirect, missing page, or server error.
      // The isolated backend is intentionally absent: the roster must render its explicit 503 fallback.
      if (status !== route.expectedStatus) failures.push(`${route.path} ${viewport.name}: HTTP ${status}, expected ${route.expectedStatus}`);
      if (metrics.scrollWidth > metrics.clientWidth + 2) failures.push(`${route.path} ${viewport.name}: horizontal overflow ${metrics.scrollWidth} > ${metrics.clientWidth}`);
      if (metrics.main < 1 && route.name !== 'app-login') failures.push(`${route.path} ${viewport.name}: missing main landmark`);
      if (!metrics.title.trim()) failures.push(`${route.path} ${viewport.name}: empty document title`);
      await page.close();
    }
    await context.close();
  }
  let installation = [];
  try { installation = await verifyAppInstall(browser, base, artifacts); }
  catch (error) { failures.push('App installation: ' + (error.stack ?? error.message)); }
  await writeFile(path.join(artifacts, 'results.json'), JSON.stringify({ results, installation, failures }, null, 2) + '\n');
  console.log(JSON.stringify({results:results.length,installation,failures},null,2));
  assert.equal(failures.length,0,'UI viewport smoke detected regressions; inspect results.json and screenshots');
} finally {
  await browser?.close();
  if (server && server.exitCode === null) {
    const stopped = once(server,'exit');
    server.kill('SIGTERM');
    await Promise.race([stopped,delay(3000).then(()=>server.kill('SIGKILL'))]);
  }
  if (ownArtifacts) await rm(artifacts,{recursive:true,force:true});
}
