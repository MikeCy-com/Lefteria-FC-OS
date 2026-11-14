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
  { name:'app-login', path:'/app/login', expectedStatus:200 }
];
const viewports = [
  { name:'phone', width:390, height:844 },
  { name:'tablet', width:768, height:1024 },
  { name:'desktop', width:1440, height:900 },
  { name:'4k', width:3840, height:2160 }
];
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
  await writeFile(path.join(artifacts, 'results.json'), JSON.stringify({ results, failures }, null, 2) + '\n');
  console.log(JSON.stringify({results:results.length,failures},null,2));
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
