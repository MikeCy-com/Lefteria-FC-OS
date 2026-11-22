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
  { name:'news', path:'/news', expectedStatus:200 },
  { name:'news-academy', path:'/news?scope=academy', expectedStatus:200 },
  { name:'shop', path:'/shop', expectedStatus:200 },
  { name:'sponsors-unavailable', path:'/sponsors', expectedStatus:503 },
  { name:'partners-unavailable', path:'/partners', expectedStatus:503 },
  { name:'events-unavailable', path:'/events', expectedStatus:503 },
  { name:'seasons-unavailable', path:'/seasons', expectedStatus:503 },
  { name:'academy', path:'/academy', expectedStatus:200 },
  { name:'academy-groups-unavailable', path:'/academy/groups', expectedStatus:503 },
  { name:'academy-philosophy', path:'/academy/philosophy', expectedStatus:200 },
  { name:'about', path:'/about', expectedStatus:200 },
  { name:'contact', path:'/contact', expectedStatus:200 },
  { name:'academy-registration', path:'/academy/registration', expectedStatus:200 },
  { name:'privacy', path:'/privacy', expectedStatus:200 },
  { name:'web-app-access', path:'/login', expectedStatus:200 },
  { name:'app-login', path:'/app/login', expectedStatus:200 },
  { name:'admin-login', path:'/admin/login', expectedStatus:200 }
];
const viewports = [
  { name:'phone', width:390, height:844 },
  { name:'tablet', width:768, height:1024 },
  { name:'desktop', width:1440, height:900 },
  // Actual reference compositions supplied by the club (desktop 1672×941).
  { name:'reference', width:1672, height:941 },
  { name:'4k', width:3840, height:2160 }
];

async function verifyAppInstall(browser, base, artifacts) {
  const checks = [];
  const context = await browser.newContext({ viewport:{width:390,height:844}, reducedMotion:'reduce', locale:'el-GR' });
  try {
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(base + '/', { waitUntil:'domcontentloaded' });
    // Registration is performed by the root onMount: wait for hydration, not a timer.
    await page.waitForFunction(async () => !!await navigator.serviceWorker.getRegistration('/'));
    await page.getByTestId('consent-decline').click();
    await page.getByTestId('cookie-consent').waitFor({state:'hidden'});
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
    const dialog = page.getByRole('dialog', { name:'Η επίσημη εφαρμογή Λευτεριά FC OS' });
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
    assert.equal(manifest.id,'/lefteria-fc-os');
    assert.ok(manifest.icons.some(icon => icon.sizes==='192x192' && icon.src==='/os/icon-192.png'));
    assert.ok(manifest.icons.some(icon => icon.sizes==='512x512' && icon.src==='/os/icon-512.png'));
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
    await page.goto(base + '/', { waitUntil:'domcontentloaded' });
    await page.waitForFunction(async () => !!await navigator.serviceWorker.getRegistration('/'));
    await page.getByTestId('consent-decline').click();
    await page.getByTestId('cookie-consent').waitFor({state:'hidden'});
    await page.getByTestId('official-app-install').click();
    const dialog = page.getByRole('dialog', {name:'Η επίσημη εφαρμογή Λευτεριά FC OS'});
    await dialog.waitFor({state:'visible'});
    assert.match(await dialog.innerText(), /Safari/);
    assert.match(await dialog.innerText(), /Προσθήκη στην οθόνη Αφετηρίας/);
    await page.screenshot({path:path.join(artifacts,'app-install-ios-guidance.png'),fullPage:true});
    assert.equal(await page.getByTestId('official-android-apk').count(), 0);
    checks.push('iPhone browser guidance without offering an Android APK');
  } finally { await apple.close(); }

  const android = await browser.newContext({
    viewport:{width:390,height:844}, isMobile:true, hasTouch:true,
    userAgent:'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36'
  });
  try {
    let officialRequests = 0;
    // Never contact production during this check, even if the UI regresses.
    await android.route('https://lefteriafc.cy/**', route => { officialRequests++; return route.abort(); });
    const page = await android.newPage();
    await page.goto(base + '/', {waitUntil:'domcontentloaded'});
    await page.waitForFunction(async () => !!await navigator.serviceWorker.getRegistration('/'));
    await page.getByTestId('consent-decline').click();
    await page.getByTestId('cookie-consent').waitFor({state:'hidden'});
    await page.evaluate(() => {
      window.installPromptCalls = 0;
      const event = new Event('beforeinstallprompt', {cancelable:true});
      event.prompt = () => { window.installPromptCalls++; return Promise.resolve(); };
      event.userChoice = Promise.resolve({outcome:'dismissed'});
      window.dispatchEvent(event);
    });
    await page.getByTestId('official-app-install').click();
    await page.waitForFunction(() => !document.querySelector('[data-testid="official-app-install"]').disabled);
    assert.equal(await page.evaluate(() => window.installPromptCalls), 1);
    assert.equal(await page.getByTestId('official-android-apk').count(), 0);
    assert.equal(officialRequests, 0);
    await page.screenshot({path:path.join(artifacts,'app-install-android-os.png'),fullPage:true});
    checks.push('Android prompts native installation of current OS app without legacy APK or production requests');
  } finally { await android.close(); }

  return checks;
}


function colorLuminance(cssColor) {
  const numbers = cssColor.match(/[0-9.]+/g)?.slice(0,3).map(Number) ?? [];
  if (numbers.length !== 3) throw new Error('Cannot measure CSS color ' + cssColor);
  const channels = numbers.map(value => {
    const channel = value / 255;
    return channel <= .04045 ? channel / 12.92 : ((channel + .055) / 1.055) ** 2.4;
  });
  return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
}

function textContrast(foreground, background) {
  const a = colorLuminance(foreground), b = colorLuminance(background);
  return (Math.max(a,b) + .05) / (Math.min(a,b) + .05);
}

// The real admin layout + auth controller run in a browser against an isolated
// Vite server. A synthetic authorized /auth/me response exercises the actual
// authenticated rendering path, without credentials or production API access.
// This does not substitute for a real staging auth/session penetration test.
async function verifyAuthenticatedAdminNavigation(browser, base, artifacts) {
  const measurements = [];
  const session = {
    user:{
      id:'11111111-1111-4111-8111-111111111111',username:'visual-admin',name:'Visual Audit',
      role:'super_admin',permissions:['*:*'],
      organizations:[
        {id:'lefteria_fc',name:'Lefteria FC'},
        {id:'lefteria_fc_academy',name:'Lefteria FC Academy'}
      ]
    },
    expires_at:'2090-01-01T00:00:00Z'
  };
  for (const viewport of [
    {name:'phone',width:390,height:844},
    {name:'tablet',width:768,height:1024},
    {name:'desktop',width:1440,height:900},
    {name:'4k',width:3840,height:2160}
  ]) {
    const context = await browser.newContext({
      viewport:{width:viewport.width,height:viewport.height},
      locale:'el-GR',colorScheme:'dark',reducedMotion:'reduce'
    });
    try {
      const page = await context.newPage();
      await page.route('**/api/admin/**',route => {
        const pathname = new URL(route.request().url()).pathname;
        if (pathname === '/api/admin/auth/me') {
          return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(session)});
        }
        return route.fulfill({status:503,contentType:'application/json',body:'{"code":"isolated_backend_unavailable"}'});
      });
      const response = await page.goto(base+'/admin',{waitUntil:'domcontentloaded',timeout:30000});
      assert.equal(response?.status(),200);
      const sidebar = page.getByTestId('admin-structured-navigation');
      await sidebar.waitFor({state:'visible',timeout:20000});
      // The new sidebar is deliberately compact: only work-area headers show
      // until a user opens a permission-scoped, single-level disclosure.
      const websiteCategory = page.getByTestId('admin-website-category');
      await websiteCategory.waitFor({state:'visible'});
      assert.equal(await page.getByTestId('admin-news-link').isVisible(),false,
        'Collapsed website category must hide its child links');
      await websiteCategory.click();
      await page.getByTestId('admin-news-link').waitFor({state:'visible'});
      assert.equal(await page.getByTestId('admin-hub-link').isVisible(),false,
        'Unselected sections should remain collapsed by default');
      await page.evaluate(() => document.fonts.ready);
      const values = await sidebar.evaluate(nav => {
        const tool = nav.querySelector('[data-testid="admin-news-link"]');
        const section = nav.querySelector('[data-testid="admin-website-navigation"] .admin-nav-section');
        const category = nav.querySelector('[data-testid="admin-website-category"]');
        if (!tool || !section || !category) throw new Error('Missing authorized navigation elements');
        const linkCss = getComputedStyle(tool), navCss = getComputedStyle(nav);
        const navRect = nav.getBoundingClientRect();
        return {
          fontSize:parseFloat(linkCss.fontSize),
          sectionSize:parseFloat(getComputedStyle(section).fontSize),
          categorySize:parseFloat(getComputedStyle(category).fontSize),
          linkHeight:tool.getBoundingClientRect().height,
          categoryHeight:category.getBoundingClientRect().height,
          linkColor:linkCss.color,navColor:navCss.backgroundColor,
          sidebarWidth:navRect.width,
          sidebarScrollWidth:nav.scrollWidth, sidebarClientWidth:nav.clientWidth,
          pageWidth:document.documentElement.scrollWidth,viewportWidth:document.documentElement.clientWidth,
          linkCount:nav.querySelectorAll('a').length,
          openCount:nav.querySelectorAll('details[open]').length
        };
      });
      // Every authenticated admin route uses the same full-width content rail.
      // The usable pane must not stop at an arbitrary 1200–1800px cap or have
      // unequal left/right padding. Validate on phone, tablet, desktop and 4K.
      const contentMetrics = await page.locator('#admin-main').evaluate(main => {
        const parent=main.parentElement;
        const rect=main.getBoundingClientRect();
        const parentRect=parent.getBoundingClientRect();
        const css=getComputedStyle(main);
        return {
          width:rect.width, availableWidth:parentRect.right - rect.left,
          padLeft:parseFloat(css.paddingLeft),padRight:parseFloat(css.paddingRight),
          rightGap:parentRect.right - rect.right,
          boxSizing:css.boxSizing
        };
      });
      assert.ok(Math.abs(contentMetrics.rightGap)<=2,
        viewport.name+' admin content does not fill its available width');
      assert.ok(Math.abs(contentMetrics.padLeft-contentMetrics.padRight)<=1,
        viewport.name+' admin left and right gutters differ');
      assert.equal(contentMetrics.boxSizing,'border-box');
      assert.ok(contentMetrics.width>0);
      assert.ok(values.fontSize >= 14,
        viewport.name+' nav child link font is too small');
      assert.ok(values.sectionSize >= 14,
        viewport.name+' nav section link font is too small');
      assert.ok(values.categorySize >= (viewport.width <= 720 ? 16 : 17),
        viewport.name+' nav category heading is too small');
      assert.ok(values.categorySize > values.fontSize,
        viewport.name+' category heading must be larger than its links');
      assert.ok(values.linkHeight >= 44 && values.categoryHeight >= 44,
        viewport.name+' navigation targets must reach 44px');
      assert.equal(values.openCount,1,viewport.name+' only the selected work area should be open');
      assert.ok(values.linkCount >= 12,'All granted admin modules should remain reachable');
      assert.ok(textContrast(values.linkColor,values.navColor) >= 4.5,
        viewport.name+' navigation contrast below WCAG AA');
      assert.ok(values.pageWidth <= values.viewportWidth + 2,
        viewport.name+' authenticated admin overflows document');
      if (viewport.width <= 720)
        assert.ok(values.sidebarScrollWidth <= values.sidebarClientWidth + 2,
          'Mobile admin menu should not depend on an overflowing horizontal link rail');
      else assert.ok(values.sidebarWidth >= (viewport.width <= 920 ? 250 : 260),
        'Sidebar rail became too narrow');
      if (viewport.width > 720) {
        const expectedWidth = viewport.width >= 2560 ? 280 : viewport.width <= 920 ? 250 : 260;
        assert.ok(Math.abs(values.sidebarWidth - expectedWidth) <= 2,
          viewport.name+' admin sidebar width no longer matches shared compact rail');
      }
      const news = page.getByTestId('admin-news-link');
      // :focus-visible intentionally follows keyboard modality. A synthetic
      // focus() call immediately after clicking the disclosure is pointer
      // modality in Chromium, not an accessible keyboard interaction.
      await news.focus();
      await page.keyboard.press('Tab');
      await page.keyboard.press('Shift+Tab');
      const focus = await news.evaluate(link => {
        const css = getComputedStyle(link);
        return {focused:document.activeElement === link, outlineWidth:parseFloat(css.outlineWidth),outlineStyle:css.outlineStyle};
      });
      assert.ok(focus.focused && focus.outlineWidth >= 3 && focus.outlineStyle !== 'none',
        viewport.name + ' nav keyboard focus is not visible');
      await page.keyboard.press('Tab');
      assert.equal(await page.evaluate(() => document.activeElement?.tagName), 'A',
        'Keyboard Tab should move to the next accessible navigation link');
      const screenshot = 'admin-auth-sidebar-' + viewport.name + '.png';
      await page.screenshot({path:path.join(artifacts,screenshot),fullPage:true,animations:'disabled',timeout:30000});
      measurements.push({viewport:viewport.name,screenshot,...values,contrast:Math.round(textContrast(values.linkColor,values.navColor)*100)/100});
    } finally {await context.close();}
  }
  // The same controller must not expose unrelated modules to an Academy editor
  // without the finance/club/football grants.
  const context = await browser.newContext({viewport:{width:1440,height:900}});
  try {
    const page = await context.newPage();
    const editor = {...session,user:{...session.user,role:'editor',permissions:['news:read'],
      organizations:[{id:'lefteria_fc_academy',name:'Lefteria FC Academy'}]}};
    await page.route('**/api/admin/**',route => route.fulfill({
      status:new URL(route.request().url()).pathname==='/api/admin/auth/me'?200:503,
      contentType:'application/json',body:JSON.stringify(new URL(route.request().url()).pathname==='/api/admin/auth/me'?editor:{code:'unavailable'})
    }));
    await page.goto(base+'/admin',{waitUntil:'domcontentloaded'});
    await page.getByTestId('admin-website-category').click();
    await page.getByTestId('admin-news-link').waitFor({state:'visible'});
    assert.equal(await page.getByTestId('admin-payabl-link').count(),0,'Editor must not see finance');
    assert.equal(await page.getByTestId('admin-groups-link').count(),0,'Editor must not see Academy groups without grants');
    assert.equal(await page.getByTestId('admin-club-link').count(),0,'Editor must not see club settings without grants');
    measurements.push({viewport:'restricted-academy-editor',permissionGates:'verified'});
  } finally {await context.close();}
  return measurements;
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
      // Synthetic anonymous response exercises the actual login form, without credentials or a backend.
      if (route.name === 'admin-login') {
        await page.route('**/api/admin/auth/me', request => request.fulfill({status:401,contentType:'application/json',body:'{}'}));
      }
      const response = await page.goto(url, { waitUntil:'domcontentloaded', timeout:30000 });
      await page.locator('body').waitFor();
      if (route.name === 'admin-login') await page.getByTestId('admin-login-username').waitFor();
      if (route.name === 'app-login') await page.getByTestId('phone-step').waitFor();
      await page.evaluate(() => document.fonts.ready);
      const typography = await page.evaluate(async () => {
        const faces = await document.fonts.load('italic 800 32px "Fira Sans Extra Condensed"', 'ΛΕΥΤΕΡΙΑ');
        return {
          italicLoaded: faces.length > 0 && faces.every(face => face.status === 'loaded'),
          headings: [...document.querySelectorAll('h1,h2')].slice(0,12).map(element => {
            const style = getComputedStyle(element);
            return {text:element.textContent?.slice(0,80),family:style.fontFamily,size:style.fontSize,weight:style.fontWeight,style:style.fontStyle,lineHeight:style.lineHeight};
          })
        };
      });
      if (!typography.italicLoaded) failures.push(route.path + ' ' + viewport.name + ': real Greek italic font did not load');
      // Lefteria-FC source public skin uses Fira Sans body and condensed Fira
      // display: keep 16px reading baseline and 38px/58px catalog headings.
      // A former live-screenshot snapshot used Manrope; the source-repository
      // CSS is now the explicit migration reference.
      const publicFont = await page.evaluate(() => {
        const root = document.querySelector('.public-site');
        const headline = document.querySelector('[data-testid="academy-groups-hero"] h1, .catalog-heading h1');
        return root ? {
          family:getComputedStyle(root).fontFamily,
          size:parseFloat(getComputedStyle(root).fontSize),
          headlineSize:headline ? parseFloat(getComputedStyle(headline).fontSize) : null
        } : null;
      });
      if(publicFont) {
        if(!publicFont.family.replace(/^['\"]/, '').startsWith('Fira Sans') || publicFont.size !== 16)
          failures.push(route.path+' '+viewport.name+': source public Fira Sans 16px mismatch: '+JSON.stringify(publicFont));
        if(publicFont.headlineSize !== null) {
          const originalSize = viewport.width <= 650 ? 38 : 58;
          if(Math.abs(publicFont.headlineSize-originalSize) > .5)
            failures.push(route.path+' '+viewport.name+': measured original heading '+originalSize+'px vs '+publicFont.headlineSize+'px');
        }
      }
      // Public footer navigation is contextual: Academy pages cannot inherit
      // the First Team's single-city banner or links to its roster/results.
      if(route.name === 'academy' || route.name === 'first-team'){
        const footer = page.getByTestId('footer');
        const footerScope = await footer.getAttribute('data-organization');
        const bannerLabel = (await page.getByTestId('public-city-banner').locator('strong').innerText()).trim();
        if(route.name === 'academy'){
          if(footerScope !== 'lefteria_fc_academy' || bannerLabel !== 'ΑΚΑΔΗΜΙΑ' ||
              await page.getByTestId('footer-nav-team').count() !== 0 ||
              await page.getByTestId('footer-nav-groups').count() !== 1)
            failures.push('Academy: footer must keep verified Academy city/link scope '+JSON.stringify({footerScope,bannerLabel}));
        }else if(footerScope !== 'lefteria_fc' || bannerLabel !== 'ΛΕΜΕΣΟΣ' ||
            await page.getByTestId('footer-nav-team').count() !== 1){
          failures.push('First Team: footer must keep First Team navigation '+JSON.stringify({footerScope,bannerLabel}));
        }
      }
      const metrics = await page.evaluate(() => ({
        scrollWidth:document.documentElement.scrollWidth,
        clientWidth:document.documentElement.clientWidth,
        title:document.title,
        main:document.querySelectorAll('main').length
      }));
      // Typography audit: source screenshots exposed oversized display type.
      // Check actual computed CSS, not just tokens, at every viewport including
      // phone and 4K. Body text and the fixed Academy groups baseline stay intact.
      const displayType = await page.evaluate(kind => {
        const selectors = {
          home:'.home-reference .public-hero-copy h1',
          academy:'.academy-intro h1',
          'first-team':'.team-heading:not(.matches-reference-heading) > h1',
          'team-schedule':'.matches-reference-banner h1',
          'app-login':'.login-welcome h2'
        };
        const selector = selectors[kind];
        if (!selector) return null;
        const element = document.querySelector(selector);
        return element ? {
          selector,px:Math.round(parseFloat(getComputedStyle(element).fontSize)*10)/10,
          text:element.textContent?.trim().slice(0,65)
        } : {selector,px:null};
      },route.name);
      if(displayType) {
        const caps = {
          home:viewport.width<=767?47:80,
          academy:viewport.width<=767?55:76,
          'first-team':viewport.width<=767?55:76,
          'team-schedule':viewport.width<=767?40:68,
          'app-login':viewport.width<=720?35:57
        };
        const cap=caps[route.name];
        if(displayType.px===null || !Number.isFinite(displayType.px) || displayType.px>cap)
          failures.push(route.path+' '+viewport.name+': display typography '+JSON.stringify(displayType)+' exceeds '+cap+'px');
      }
      // Source reference pictures are 1672×941. Measure actual browser
      // geometry against their major visible regions without pretending
      // that bounds alone establish photographic/pixel parity.
      let referenceLayout = null;
      if (viewport.name === 'reference' &&
          ['admin-login','app-login','academy-registration','home','contact','first-team'].includes(route.name)) {
        referenceLayout = await page.evaluate(kind => {
          const rect = selector => {
            const element = document.querySelector(selector);
            if (!element) return null;
            const r = element.getBoundingClientRect();
            return {x:Math.round(r.x),y:Math.round(r.y),
              width:Math.round(r.width),height:Math.round(r.height),
              bottom:Math.round(r.bottom),right:Math.round(r.right)};
          };
          const result = {
            viewport:{width:innerWidth,height:innerHeight},
            documentHeight:document.documentElement.scrollHeight,
            adminHeader:rect('.admin-access-header'),
            adminStory:rect('.admin-signin-story'),
            adminPanel:rect('.admin-signin-grid>.admin-panel'),
            adminAction:rect('[data-testid="admin-login-submit"]'),
            adminFooter:rect('.admin-access-footer'),
            memberSidebar:rect('.login-sidebar'),
            memberTopbar:rect('.login-topbar'),
            memberWelcome:rect('.login-welcome'),
            memberPanel:rect('.login-form-panel'),
            memberRoles:rect('.login-audience-grid'),
            memberAction:rect('[data-testid="request-otp-btn"]'),
            memberBenefits:rect('.login-benefits'),
            registrationSteps:rect('[data-testid="step-indicator"]'),
            registrationCard:rect('[data-testid="form-card"]'),
            hero:rect('.public-hero-stage,.home-hero,.hero,.public-hero'),
            heroCopy:rect('.home-reference .public-hero-copy'),
            publicRail:rect('.public-rail'),
            publicTopbar:rect('.public-header'),
            homeFixture:rect('[data-testid="public-hero-fixture"]'),
            homeTiles:rect('[data-testid="public-destinations"]'),
            homeMotto:rect('[data-testid="home-brand-interval"]'),
            homeNews:rect('[data-testid="public-home-news"]'),
            teamHeading:rect('.team-heading:not(.matches-reference-heading)'),
            teamCover:rect('[data-testid="team-heading-photo"]'),
            teamTabs:rect('.team-heading .team-tabs')
          };
          if (kind === 'app-login') result.roleButtons =
            document.querySelectorAll('.login-audience-grid button').length;
          return result;
        },route.name);
        if (route.name==='home') {
          const {hero,heroCopy:copy,homeFixture:fixture,publicRail:rail,publicTopbar:top,homeTiles:tiles,
            homeMotto:motto,homeNews:news}=referenceLayout;
          if(!hero||!rail||!top||!tiles||!motto||!news){
            failures.push('home 1672×941: one or more reference sections are missing');
          }else{
            if(rail.width<150||rail.width>160)
              failures.push('home: club rail should be 154px wide (actual '+rail.width+')');
            if(top.height<60||top.height>68)
              failures.push('home: reference topbar should be 64px tall (actual '+top.height+')');
            if(hero.y<59||hero.y>70||hero.height<400||hero.height>440)
              failures.push('home: cinematic banner is not the 410px reference region '+JSON.stringify(hero));
            if(tiles.y<hero.bottom-2||tiles.y>hero.bottom+12||tiles.height<100||tiles.height>115)
              failures.push('home: four photo tiles should follow the hero within 105px '+JSON.stringify(tiles));
            if(!fixture || !copy || fixture.width<290 || fixture.width>335 ||
               fixture.height<200 || fixture.x < hero.x+hero.width*.65 ||
               fixture.right < hero.right-100 || fixture.right > hero.right+2 ||
               fixture.x < copy.right+12)
              failures.push('home: compact next-match must remain on the right, without covering the headline or hero picture '+JSON.stringify({fixture,hero,copy}));
            if(motto.y<tiles.bottom-2 || motto.y>tiles.bottom+4 || motto.height<85 || motto.height>140)
              failures.push('home: compact club identity band must follow four photo tiles before news '+JSON.stringify(motto));
            if(news.y<motto.bottom-2||news.y>motto.bottom+100)
              failures.push('home: CMS news must follow the club motto and preserve source image section order '+JSON.stringify({news,motto}));
            if(news.y<tiles.bottom+70 || news.y>900)
              failures.push('home: editorial news should follow the compact identity band within the first viewport');
            if(Math.abs(top.x-rail.width)>2)
              failures.push('home: topbar should begin at the rail boundary');
          }
        }
        if (route.name==='first-team') {
          const {teamHeading,teamCover,teamTabs}=referenceLayout;
          if(!teamHeading||!teamCover||!teamTabs ||
            teamHeading.height<320 ||
            teamCover.height<teamHeading.height-2 ||
            teamCover.width<teamHeading.width-2 ||
            teamTabs.height<44 ||
            teamTabs.bottom>teamHeading.bottom+4) {
            failures.push('first-team: photographic club hero and working tabs are not aligned '+JSON.stringify({teamHeading,teamCover,teamTabs}));
          }
        }
        if (route.name==='admin-login') {
          const {adminHeader:header,adminPanel:panel,adminAction:action,adminFooter:footer,documentHeight} = referenceLayout;
          if (!header||!panel||!action||!footer)
            failures.push('admin login 1672×941: missing reference sections');
          else {
            if (header.height<50||header.height>85)
              failures.push('admin login: reference topbar differs sharply in height');
            if (panel.x<825 || panel.width<560 || panel.x+panel.width>1680)
              failures.push('admin login: right login panel is not in the reference column');
            if (panel.y>150 || panel.bottom>875)
              failures.push('admin login: panel obscured below the reference viewport');
            if (action.bottom>890)
              failures.push('admin login: submit action requires the user to scroll');
            if (footer.y>910)
              failures.push('admin login: bottom support/footer strip is below the viewport');
            if (documentHeight>viewport.height+3)
              failures.push('admin login: page scrolls in reference desktop viewport ('+documentHeight+' > '+viewport.height+')');
          }
        }
        if (route.name==='app-login') {
          const {memberSidebar:rail,memberTopbar:top,memberWelcome:welcome,
            memberPanel:panel,memberRoles:roles,memberAction:action,memberBenefits:benefits,roleButtons} = referenceLayout;
          if(!rail||!top||!welcome||!panel||!roles||!action||!benefits)
            failures.push('member login: missing photographic/role or benefits reference sections');
          else {
            if(rail.width<140||rail.width>160)
              failures.push('member login: reference vertical club navigation is not ~154px');
            if(top.height<50||top.height>85)
              failures.push('member login: top header height is not the reference size');
            if(panel.x<800||panel.width<570||panel.right>1672)
              failures.push('member login: role/OTP form outside the reference right column');
            if(panel.y>180||roles.y>410)
              failures.push('member login: role choices not visible at top of form');
            if(roleButtons!==3)
              failures.push('member login: missing one of the three photographic audience cards');
            if(action.bottom>850)
              failures.push('member login: OTP request is below first reference viewport');
            if(benefits.y>960)
              failures.push('member login: reference app benefits section pushed below the fold');
          }
        }
      }
      // Record real computed styles per public page and section; a single
      // global CSS assertion cannot detect oversized nested headings, gutters
      // or inconsistent card padding. Evidence stays in isolated CI artifacts.
      const sectionDesign = await page.evaluate(() => {
        const site = document.querySelector('.public-site');
        if (!site) return null;
        const details = (node) => {
          const css = getComputedStyle(node);
          const box = node.getBoundingClientRect();
          return {
            tag:node.tagName.toLowerCase(),
            className:typeof node.className==='string'?node.className.slice(0,120):'',
            fontFamily:css.fontFamily,fontSize:css.fontSize,fontWeight:css.fontWeight,
            fontStyle:css.fontStyle,lineHeight:css.lineHeight,letterSpacing:css.letterSpacing,
            color:css.color,backgroundColor:css.backgroundColor,
            padding:[css.paddingTop,css.paddingRight,css.paddingBottom,css.paddingLeft],
            margin:[css.marginTop,css.marginRight,css.marginBottom,css.marginLeft],
            width:Math.round(box.width),left:Math.round(box.left),
            maxWidth:css.maxWidth
          };
        };
        const candidates = [
          '.public-header','.public-rail','.public-main','.public-footer',
          '.public-page-heading','.public-section','.section-heading',
          '.public-news-grid','.public-group-grid','.public-group-card',
          '.article-body','.public-hero-stage'
        ];
        const sections = candidates.flatMap(selector =>
          [...site.querySelectorAll(selector)].slice(0,20).map(element => ({
            selector,...details(element),
            heading:(element.matches('h1,h2,h3')?element:
              element.querySelector('h1,h2,h3')) || null
          })).map(({heading,...item})=>({
            ...item,heading:heading?details(heading):null
          }))).slice(0,90);
        return {root:details(site),sections};
      });
      if (sectionDesign && (!sectionDesign.root.fontFamily.includes('Fira Sans') ||
          !sectionDesign.root.color || sectionDesign.root.width<=0)) {
        failures.push(route.path+' '+viewport.name+': invalid public design root');
      }
      if (route.name==='about' && sectionDesign) {
        const aboutHeadings=sectionDesign.sections
          .filter(item=>item.selector==='.section-heading' && item.heading)
          .map(item=>parseFloat(item.heading.fontSize));
        if(aboutHeadings.length!==2 || aboutHeadings.some(size=>size<20||size>34))
          failures.push('About '+viewport.name+': section headings differ from the shared Academy hierarchy: '+JSON.stringify(aboutHeadings));
      }
      const screenshot = `${route.name}-${viewport.name}.png`;
      await page.screenshot({path:path.join(artifacts,screenshot),fullPage:true,animations:'disabled',timeout:30000});
      const status = response?.status() ?? 0;
      results.push({route:route.path,viewport:viewport.name,width:viewport.width,status,...metrics,typography,publicFont,displayType,referenceLayout,sectionDesign,screenshot});
      // Status assertions distinguish real rendering from a redirect, missing page, or server error.
      // The isolated backend is intentionally absent: the roster must render its explicit 503 fallback.
      if (status !== route.expectedStatus) failures.push(`${route.path} ${viewport.name}: HTTP ${status}, expected ${route.expectedStatus}`);
      if (metrics.scrollWidth > metrics.clientWidth + 2) failures.push(`${route.path} ${viewport.name}: horizontal overflow ${metrics.scrollWidth} > ${metrics.clientWidth}`);
      if (metrics.main < 1 && route.name !== 'app-login') failures.push(`${route.path} ${viewport.name}: missing main landmark`);
      if (!metrics.title.trim()) failures.push(`${route.path} ${viewport.name}: empty document title`);
      // Real Chromium search check at phone + reference desktop widths.
      // Search remains organization-specific even when the news API is absent.
      if (['academy','first-team'].includes(route.name) &&
          ['phone','reference'].includes(viewport.name)) {
        // The audit navigates with DOMContentLoaded for screenshot speed.
        // Wait for client hydration before exercising a real event handler,
        // then retry a few times instead of mistaking SSR markup for a click.
        await page.waitForLoadState('load',{timeout:12000}).catch(()=>{});
        const trigger=page.getByTestId('public-search-open');
        const dialog=page.getByTestId('public-search-dialog');
        let opened=false;
        for(let attempt=0;attempt<3 && !opened;attempt++){
          await trigger.click();
          try {await dialog.waitFor({state:'visible',timeout:2000});opened=true;}
          catch { /* allow client hydration to complete */ }
        }
        if(!opened) {
          failures.push(route.name+' '+viewport.name+': search did not open after client hydration');
        }else{
        const contextText=await page.getByTestId('public-search-context').innerText();
        const input=page.getByTestId('public-search-input');
        if(route.name==='academy'){
          if(!contextText.includes('Ακαδημία'))
            failures.push('Academy search context is not Academy-scoped on '+viewport.name);
          await input.fill('ΑΠΟΤΕΛΕΣΜΑΤΑ');
          if(await page.getByTestId('public-search-results').locator('a[href="/team?tab=results"]').count()!==0)
            failures.push('Academy search exposed First Team results on '+viewport.name);
          await input.fill('Ηλικιακά τμήματα');
          if(await page.getByTestId('public-search-result-academy-groups').count()!==1)
            failures.push('Academy search lost its age-group directory on '+viewport.name);
        }else{
          if(!contextText.includes('Πρώτη ομάδα'))
            failures.push('First Team search context is not club-scoped on '+viewport.name);
          await input.fill('ΑΠΟΤΕΛΕΣΜΑΤΑ');
          if(await page.getByTestId('public-search-results').locator('a[href="/team?tab=results"]').count()!==1)
            failures.push('First Team search lost verified match results link on '+viewport.name);
          await input.fill('Ηλικιακά τμήματα');
          if(await page.getByTestId('public-search-result-academy-groups').count()!==0)
            failures.push('First Team search exposed Academy groups on '+viewport.name);
        }
        const countText=await page.getByTestId('public-search-count').innerText();
        if(!/(?:αποτέλεσμα|αποτελέσματα)/u.test(countText))
          failures.push('Search live result counter is absent on '+viewport.name);
        // Preserve a real, inspectable screenshot of the open search surface.
        await input.fill(route.name==='academy' ? 'Ηλικιακά τμήματα' : 'ΑΠΟΤΕΛΕΣΜΑΤΑ');
        await page.screenshot({
          path:path.join(artifacts,route.name+'-search-'+viewport.name+'.png'),
          animations:'disabled',timeout:30000
        });
        await page.keyboard.press('Escape');
        await page.getByTestId('public-search-dialog').waitFor({state:'hidden'});
        }
      }
      await page.close();
    }
    await context.close();
  }
  let installation = [];
  try { installation = await verifyAppInstall(browser, base, artifacts); }
  catch (error) { failures.push('App installation: ' + (error.stack ?? error.message)); }
  let adminNavigation=[];
  try { adminNavigation=await verifyAuthenticatedAdminNavigation(browser,base,artifacts); }
  catch(error) { failures.push('Authenticated admin sidebar visual check: '+(error.stack ?? error.message)); }
  await writeFile(path.join(artifacts, 'results.json'), JSON.stringify({ results, installation, adminNavigation, failures }, null, 2) + '\n');
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
