// Renders five disconnected role UI components in a temporary loopback-only route.
// This deliberately does NOT authenticate, seed private data, or override any access guards.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { existsSync } from 'node:fs';
import { mkdir, readFile, writeFile, rm } from 'node:fs/promises';
import { createServer } from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { chromium } from 'playwright';

const frontend = fileURLToPath(new URL('../', import.meta.url));
const auditRoute = path.join(frontend, 'src', 'routes', 'visual-role-audit');
const fixture = path.join(frontend, 'scripts', 'fixtures', 'role-visual-preview.svelte');
const artifacts = process.env.LEFTERIA_UI_ARTIFACTS;
assert.ok(artifacts, 'Set LEFTERIA_UI_ARTIFACTS to an isolated CI artifact directory.');
assert.ok(!existsSync(auditRoute), 'Refusing to overwrite an existing application route.');
assert.ok(path.resolve(auditRoute).startsWith(path.resolve(frontend, 'src', 'routes') + path.sep));
const roles = ['parent', 'player', 'coach', 'fan', 'management'];
const viewports = [{name:'phone',width:390,height:844},{name:'tablet',width:768,height:1024}];
let server;
let browser;
const results = [];
const failures = [];
try {
  await mkdir(auditRoute);
  await writeFile(path.join(auditRoute, '+page.svelte'), await readFile(fixture, 'utf8'), {flag:'wx'});
  await mkdir(artifacts, {recursive:true});
  const listener = createServer();
  listener.listen(0, '127.0.0.1');
  await once(listener, 'listening');
  const port = listener.address().port;
  await new Promise((resolve,reject)=>listener.close(error=>error?reject(error):resolve()));
  const base='http://127.0.0.1:' + port;
  server=spawn(process.execPath,['node_modules/vite/bin/vite.js','--host','127.0.0.1','--port',String(port),'--strictPort'],{
    cwd:frontend,stdio:['ignore','pipe','pipe'],windowsHide:true,
    env:{...process.env,INTERNAL_API_ORIGIN:'http://127.0.0.1:1',HOST:'127.0.0.1'}
  });
  let logs='';
  server.on('error', error => { logs += String(error); });
  for (const stream of [server.stdout,server.stderr])
    stream.on('data',chunk=>{logs=(logs+chunk.toString()).slice(-6000);});
  let ready=false;
  for(let attempt=0;attempt<120;attempt++){
    assert.equal(server.exitCode,null,'Preview server stopped: '+logs);
    try {
      const response=await fetch(base+'/visual-role-audit?role=fan',{signal:AbortSignal.timeout(1500)});
      if(response.ok){ready=true;break;}
    } catch { /* Waiting for local Vite startup. */ }
    await delay(250);
  }
  assert.ok(ready,'Preview route did not start: '+logs);
  browser=await chromium.launch({headless:true});
  for(const viewport of viewports){
    const context=await browser.newContext({
      viewport:{width:viewport.width,height:viewport.height},deviceScaleFactor:1,
      locale:'el-GR',colorScheme:'dark',reducedMotion:'reduce'
    });
    for(const role of roles){
      const page=await context.newPage();
      const response=await page.goto(base+'/visual-role-audit?role='+role,{waitUntil:'networkidle',timeout:30000});
      await page.locator('[data-testid="mobile-app"]').waitFor({timeout:15000});
      await page.evaluate(()=>document.fonts.ready);
      const snapshot=await page.evaluate(()=>({
        role:document.querySelector('[data-testid="mobile-app"]')?.getAttribute('data-mobile-role'),
        scrollWidth:document.documentElement.scrollWidth,
        clientWidth:document.documentElement.clientWidth,
        navigationCount:document.querySelectorAll('[data-testid^="bottom-tab-"]').length,
        mainCount:document.querySelectorAll('main').length,
        title:document.title
      }));
      const name='role-'+role+'-'+viewport.name+'.png';
      await page.screenshot({path:path.join(artifacts,name),fullPage:true,animations:'disabled'});
      const status=response?.status()??0;
      results.push({role,viewport:viewport.name,status,...snapshot,screenshot:name,kind:'isolated-component-not-authentication'});
      if(status!==200)failures.push(role+'/'+viewport.name+': HTTP '+status);
      if(snapshot.role!==role)failures.push(role+'/'+viewport.name+': wrong role rendered');
      if(snapshot.mainCount!==1)failures.push(role+'/'+viewport.name+': expected one main landmark');
      if(snapshot.navigationCount<4)failures.push(role+'/'+viewport.name+': missing primary tab controls');
      if(snapshot.scrollWidth>snapshot.clientWidth+2)
        failures.push(role+'/'+viewport.name+': horizontal overflow '+snapshot.scrollWidth+' > '+snapshot.clientWidth);
      await page.getByTestId('role-switcher').click();
      await page.getByTestId('switch-role-'+role).waitFor();
      const choices=await page.locator('[data-testid^="switch-role-"]').count();
      if(choices!==1)failures.push(role+'/'+viewport.name+': unverified alternative identity visible');
      await page.close();
    }
    await context.close();
  }
  await writeFile(path.join(artifacts,'role-results.json'),JSON.stringify({results,failures},null,2)+'\n');
  console.log(JSON.stringify({roleSnapshots:results.length,failures}));
  assert.equal(failures.length,0,'Role component preview regressions found; inspect screenshot artifacts.');
} finally {
  await browser?.close();
  if(server && server.exitCode===null) {
    const stopped=once(server,'exit');
    server.kill('SIGTERM');
    await Promise.race([stopped,delay(3000).then(()=>server.kill('SIGKILL'))]);
  }
  if(existsSync(auditRoute)) await rm(auditRoute,{recursive:true,force:true});
}
