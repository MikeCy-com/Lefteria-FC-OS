// Browser denial-path test: cached roles and expired tokens cannot unlock the app.
// No real credentials, identities, permission bypass, private data or remote hosts.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { mkdir, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';
import { chromium } from 'playwright';

const frontend=fileURLToPath(new URL('../',import.meta.url));
const artifacts=process.env.LEFTERIA_UI_ARTIFACTS;
assert.ok(artifacts,'Run only with an explicit isolated artifact directory.');
const roles=['parent','player','coach','fan','management'];
const privateTabs=['calendar','chat','children','financials','profile'];
let server, browser;
let logs='';
const cases=[];
const failures=[];
try {
  await mkdir(artifacts,{recursive:true});
  const probe=createServer();
  probe.listen(0,'127.0.0.1');
  await once(probe,'listening');
  const port=probe.address().port;
  await new Promise((resolve,reject)=>probe.close(e=>e?reject(e):resolve()));
  const origin='http://127.0.0.1:'+port;
  server=spawn(process.execPath,['node_modules/vite/bin/vite.js','--host','127.0.0.1','--port',String(port),'--strictPort'],{
    cwd:frontend,stdio:['ignore','pipe','pipe'],windowsHide:true,
    env:{...process.env,INTERNAL_API_ORIGIN:'http://127.0.0.1:1',HOST:'127.0.0.1'}
  });
  server.on('error',e=>{logs+=String(e);});
  for(const stream of [server.stdout,server.stderr])
    stream.on('data',chunk=>{logs=(logs+chunk.toString()).slice(-4000);});
  let ready=false;
  for(let i=0;i<120;i++){
    assert.equal(server.exitCode,null,'Isolated Vite server exited: '+logs);
    try {
      const result=await fetch(origin+'/app/login',{signal:AbortSignal.timeout(1000)});
      if(result.ok){ready=true;break;}
    } catch { /* local process is still starting */ }
    await delay(250);
  }
  assert.ok(ready,'Isolated Vite server never started: '+logs);
  browser=await chromium.launch({headless:true});
  // A fresh device must be able to browse without an OTP or stored token.
  const guestContext=await browser.newContext({viewport:{width:390,height:844},locale:'el-GR'});
  let guestPrivateRequests=0;
  await guestContext.route('**/api/mobile/**',async request=>{
    guestPrivateRequests++;
    await request.fulfill({status:403,contentType:'application/json',body:JSON.stringify({detail:'Not authorized'})});
  });
  const guestPage=await guestContext.newPage();
  const guestResponse=await guestPage.goto(origin+'/app',{waitUntil:'domcontentloaded'});
  assert.equal(guestResponse?.status(),200);
  await guestPage.getByTestId('mobile-guest-app').waitFor();
  assert.equal(new URL(guestPage.url()).pathname,'/app');
  assert.equal(await guestPage.getByTestId('mobile-app').count(),0);
  assert.equal(await guestPage.getByTestId('mobile-create-post').count(),0);
  assert.equal(await guestPage.locator('[data-mobile-role]').count(),0);
  assert.equal(await guestPage.getByTestId('guest-public-news').getAttribute('href'),'/news');
  assert.equal(await guestPage.getByTestId('guest-public-matches').getAttribute('href'),'/team?tab=results');
  assert.equal(await guestPage.getByTestId('guest-public-potm').getAttribute('href'),'/vote');
  assert.equal(await guestPage.getByTestId('guest-restricted-potm').getAttribute('href'),'/app/login?return_tab=potm');
  assert.equal(await guestPage.getByTestId('mobile-potm-ballot').count(),0);
  await guestPage.getByTestId('guest-restricted-profile').click();
  await guestPage.waitForURL(url=>url.pathname==='/app/login'&&url.searchParams.get('return_tab')==='profile');
  await guestPage.getByTestId('login-intended-tab').waitFor();
  assert.equal(await guestPage.getByTestId('mobile-app').count(),0);
  await guestPage.getByTestId('continue-as-guest').click();
  await guestPage.getByTestId('mobile-guest-app').waitFor();
  assert.equal(guestPrivateRequests,0,'Guest browsing must never call private mobile APIs');
  await guestContext.close();
  for(const role of roles){
    const context=await browser.newContext({viewport:{width:390,height:844},locale:'el-GR'});
    // A deliberately invalid token and forged cached role hint are the sole input.
    // All auth requests are rejected locally; no test traffic can reach a real API.
    await context.addInitScript(({role})=>{
      localStorage.setItem('mobile_token','intentionally-invalid-visual-audit-token');
      localStorage.setItem('mobile_available_roles',JSON.stringify(
        ['parent','player','coach','fan','management',role]));
    },{role});
    // Broad fallback first: Playwright gives the most recently registered route
    // precedence, so the auth-specific 401 handler below must win.
    let unexpectedPrivateRequests=0;
    await context.route('**/api/mobile/**',async request=>{
      unexpectedPrivateRequests++;
      await request.fulfill({status:403,contentType:'application/json',
        body:JSON.stringify({detail:'Not authorized'})});
    });
    let authChecks=0;
    await context.route('**/api/mobile/auth/**',async request=>{
      authChecks++;
      await request.fulfill({status:401,contentType:'application/json',
        headers:{'Cache-Control':'no-store'},
        body:JSON.stringify({detail:'The browser audit token is invalid',code:'session_expired'})});
    });
    for(const tab of privateTabs){
      const page=await context.newPage();
      const initial=await page.goto(origin+'/app?tab='+tab,{waitUntil:'domcontentloaded',timeout:30000});
      const name=role+':'+tab;
      const status=initial?.status()??0;
      try {
        await page.waitForURL(url=>url.pathname==='/app/login',{timeout:15000});
      } catch {
        failures.push(name+': protected URL did not redirect to login after invalid auth');
      }
      const details=await page.evaluate(()=>({
        pathname:location.pathname,
        mobileShellCount:document.querySelectorAll('[data-testid="mobile-app"]').length,
        parentDashboardCount:document.querySelectorAll('[data-testid="parent-dashboard-home"]').length,
        protectedCalendarCount:document.querySelectorAll('[data-testid="mobile-calendar"]').length,
        storedRoleHints:localStorage.getItem('mobile_available_roles')
      }));
      cases.push({forgedRole:role,requestedTab:tab,status,pathname:details.pathname,
        protectedShells:details.mobileShellCount,authChecks});
      if(status!==200)failures.push(name+': public route did not SSR successfully ('+status+')');
      if(details.pathname!=='/app/login')failures.push(name+': access remained at '+details.pathname);
      if(new URL(page.url()).searchParams.get('return_tab')!==tab)failures.push(name+': intended protected tab was lost');
      if(details.mobileShellCount||details.parentDashboardCount||details.protectedCalendarCount)
        failures.push(name+': a protected UI rendered without verified auth');
      await page.close();
    }
    if(authChecks===0)failures.push(role+': no server auth verification attempted');
    if(unexpectedPrivateRequests!==0)
      failures.push(role+': private mobile API calls occurred before authentication');
    await context.close();
  }
  await writeFile(path.join(artifacts,'auth-denial-results.json'),
    JSON.stringify({kind:'client-auth-denial-with-mocked-401-not-authenticated-e2e',cases,failures},null,2)+'\n');
  console.log(JSON.stringify({denialCases:cases.length,failures}));
  assert.equal(failures.length,0,'Invalid-token access-control browser regression');
} finally {
  await browser?.close();
  if(server && server.exitCode===null) {
    const stopped=once(server,'exit');
    server.kill('SIGTERM');
    await Promise.race([stopped,delay(3000).then(()=>server.kill('SIGKILL'))]);
  }
}
