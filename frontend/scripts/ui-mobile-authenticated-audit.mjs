/**
 * Actual /app routes and actual mobile auth controller, exercised with isolated
 * loopback-only mocked GET /api/mobile/auth/me. Unlike disconnected component
 * snapshots, hydration/bootstrap/route/navigation here are real. Unlike staging
 * authentication, identities, grants and API responses are synthetic: never
 * describe this as real credential or end-to-end backend verification.
 */
import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {once} from 'node:events';
import {mkdir,writeFile} from 'node:fs/promises';
import {createServer} from 'node:net';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {setTimeout as delay} from 'node:timers/promises';
import {chromium} from 'playwright';
import {primaryTabs,allowedTabs} from '../src/lib/mobile/navigation.ts';

const frontend=fileURLToPath(new URL('../',import.meta.url));
const artifacts=process.env.LEFTERIA_UI_ARTIFACTS;
assert.ok(artifacts,'An isolated screenshot artifact directory is required');
const roles=['parent','player','coach','fan','management'];
const viewports=[
  {name:'phone',width:390,height:844},
  {name:'desktop',width:1440,height:900}
];
const roleTab={parent:'children',player:'hub',coach:'squad',fan:'gamehub',management:'teams'};
const results=[],failures=[];
let server,browser,logs='';
try {
  await mkdir(artifacts,{recursive:true});
  const portProbe=createServer();
  portProbe.listen(0,'127.0.0.1');
  await once(portProbe,'listening');
  const port=portProbe.address().port;
  await new Promise((resolve,reject)=>portProbe.close(error=>error?reject(error):resolve()));
  const origin='http://127.0.0.1:'+port;
  server=spawn(process.execPath,
    ['node_modules/vite/bin/vite.js','--host','127.0.0.1','--port',String(port),'--strictPort'],{
      cwd:frontend,stdio:['ignore','pipe','pipe'],windowsHide:true,
      env:{...process.env,INTERNAL_API_ORIGIN:'http://127.0.0.1:1',HOST:'127.0.0.1'}
    });
  server.on('error',error=>{logs+=String(error);});
  for(const stream of [server.stdout,server.stderr])
    stream.on('data',chunk=>{logs=(logs+chunk.toString()).slice(-6000);});
  let ready=false;
  for(let attempt=0;attempt<120;attempt++){
    assert.equal(server.exitCode,null,'Isolated Vite server stopped: '+logs);
    try{
      const r=await fetch(origin+'/app/login',{signal:AbortSignal.timeout(1200)});
      if(r.ok){ready=true;break;}
    }catch{}
    await delay(250);
  }
  assert.ok(ready,'Isolated app routes did not start: '+logs);
  browser=await chromium.launch({headless:true});
  for(const viewport of viewports){
    for(const role of roles){
      const context=await browser.newContext({
        viewport:{width:viewport.width,height:viewport.height},
        deviceScaleFactor:1,locale:'el-GR',colorScheme:'dark',reducedMotion:'reduce'
      });
      const identity={
        id:'11111111-1111-4111-8111-111111111111',phone:'+35799123456',
        role,name:'Verified synthetic '+role,platform:'Web/PWA',
        created_at:'2026-01-01T00:00:00Z',last_login:'2026-11-10T00:00:00Z',
        available_roles:[role],current_role_available:true,
        avatar_url:null,team_id:null,academy_group_ids:[],linked_player_ids:[]
      };
      let meChecks=0,unexpected=0;
      const bearer='Bearer mock-isolated-'+role;
      try{
        await context.addInitScript(({role})=>{
          localStorage.setItem('mobile_token','mock-isolated-'+role);
          // The cached roles are deliberately overbroad; the real auth
          // controller must replace them using only the verified /me reply.
          localStorage.setItem('mobile_available_roles',
            JSON.stringify(['parent','player','coach','fan','management']));
        },{role});
        await context.route('**/api/mobile/**',async route=>{
          const request=route.request();
          const apiPath=new URL(request.url()).pathname;
          const response={status:503,contentType:'application/json',
            headers:{'Cache-Control':'no-store'},
            body:JSON.stringify({code:'isolated_private_api_unavailable',
              detail:'No private fixture was supplied'})};
          if(request.headers()['authorization']!==bearer){
            await route.fulfill({...response,status:401,
              body:JSON.stringify({code:'invalid_bearer'})});
            return;
          }
          if(apiPath==='/api/mobile/auth/me' && request.method()==='GET'){
            meChecks++;
            await route.fulfill({...response,status:200,body:JSON.stringify(identity)});
          }else if(apiPath==='/api/mobile/management/organization' && role==='management'){
            await route.fulfill({...response,status:200,
              body:JSON.stringify({available_organizations:[],organization_id:null,
                organization_name:null,requires_organization_selection:true})});
          }else{
            unexpected++;
            await route.fulfill(response);
          }
        });
        const page=await context.newPage();
        const response=await page.goto(origin+'/app?tab=home',{
          waitUntil:'domcontentloaded',timeout:30000
        });
        assert.equal(response?.status(),200);
        const shell=page.getByTestId('mobile-app');
        await shell.waitFor({state:'visible',timeout:25000});
        await page.getByTestId(role==='parent'
          ? 'parent-dashboard-home' : role+'-dashboard-home').waitFor({
          state:'visible',timeout:25000
        });
        await page.evaluate(()=>document.fonts.ready);
        if(role==='fan')assert.equal(await page.getByTestId('fan-home-potm').getAttribute('href'),'/app?tab=potm');
        const snapshot=await page.evaluate(()=>({
          pathname:location.pathname,
          tab:new URLSearchParams(location.search).get('tab'),
          role:document.querySelector('[data-testid="mobile-app"]')?.getAttribute('data-mobile-role'),
          userName:document.querySelector('[data-testid="mobile-app"]')?.textContent?.slice(0,1200) ?? '',
          scrollWidth:document.documentElement.scrollWidth,
          clientWidth:document.documentElement.clientWidth,
          tabs:[...document.querySelectorAll('[data-testid^="bottom-tab-"]')]
            .map(el=>el.getAttribute('data-testid')?.slice('bottom-tab-'.length)),
          mainLandmarks:document.querySelectorAll('main').length,
          privateChildren:document.querySelectorAll('[data-testid="parent-home-child"]').length,
          cachedRoles:localStorage.getItem('mobile_available_roles')
        }));
        assert.equal(snapshot.pathname,'/app','Real protected route must stay at /app');
        assert.equal(snapshot.role,role,'Signed-in role must come from /me');
        assert.equal(meChecks,1,'Bootstrap should validate synthetic /me exactly once');
        assert.deepEqual(snapshot.tabs,primaryTabs(role),
          'Actual /app bottom tabs must reflect only verified role');
        assert.equal(snapshot.mainLandmarks,1);
        assert.ok(snapshot.scrollWidth<=snapshot.clientWidth+2,
          viewport.name+'/'+role+' : authenticated mobile screen overflows horizontally');
        assert.equal(snapshot.cachedRoles,JSON.stringify([role]),
          'Forged cached role hints were not replaced with verified role');
        assert.equal(snapshot.privateChildren,0,
          'No synthetic parent/guardian relationships may appear without family data');
        assert.ok(!snapshot.userName.includes('Verification private sibling sentinel'),
          'Unprovided personal data must not render');
        const switcher=page.getByTestId('role-switcher');
        await switcher.focus();
        await page.keyboard.press('Enter');
        const choices=page.locator('[data-testid^="switch-role-"]');
        await page.getByTestId('switch-role-'+role).waitFor({state:'visible'});
        assert.equal(await choices.count(),1,'Forged role hints unlocked extra choices');
        await page.keyboard.press('Escape');
        assert.equal(await switcher.getAttribute('aria-expanded'),'false');
        const screenshot='authenticated-app-'+role+'-'+viewport.name+'.png';
        await page.screenshot({path:path.join(artifacts,screenshot),
          fullPage:true,animations:'disabled',timeout:30000});
        const secondary=roleTab[role];
        assert.ok(allowedTabs(role).includes(secondary));
        await page.goto(origin+'/app?tab='+secondary,
          {waitUntil:'domcontentloaded',timeout:30000});
        await page.getByTestId('mobile-app').waitFor({state:'visible',timeout:25000});
        assert.equal(await page.getByTestId('mobile-app').getAttribute('data-mobile-role'),role,
          'Role was changed by tab navigation');
        assert.equal(await page.locator('[data-testid^="switch-role-"]').count(),0,
          'A role-choice dialog must not remain open on deep links');
        const secondaryScreenshot='authenticated-app-'+role+'-'+viewport.name+'-'+secondary+'.png';
        await page.screenshot({path:path.join(artifacts,secondaryScreenshot),
          fullPage:true,animations:'disabled',timeout:30000});
        results.push({role,viewport:viewport.name,status:200,
          screenshot,secondaryScreenshot,secondary,kind:'synthetic-me-real-protected-route',
          meChecks,unavailablePrivateRequests:unexpected,
          width:snapshot.clientWidth,scrollWidth:snapshot.scrollWidth});
        await page.close();
      }catch(error){
        failures.push(role+'/'+viewport.name+': '+(error.stack||error.message));
      }finally{await context.close();}
    }
  }
  // Actual profile navigation and controller switch flow, synthetic server
  // responses only. Verify distinct profile IDs and no extra OTP on switching.
  const profileContext=await browser.newContext({viewport:{width:390,height:844},locale:'el-GR'});
  try {
    const phone='+35799123456', grants=['parent','fan'];
    const profiles={
      parent:{id:'11111111-1111-4111-8111-111111111111',role:'parent',name:'Synthetic Parent Profile',email:'parent@example.test',favorite_team:null},
      fan:{id:'22222222-2222-4222-8222-222222222222',role:'fan',name:'Synthetic Fan Profile',email:'fan@example.test',favorite_team:'academy'}
    };
    for(const profile of Object.values(profiles)) Object.assign(profile,{phone,available_roles:grants,current_role_available:true,
      platform:'Web/PWA',created_at:'2026-01-01T00:00:00Z',last_login:'2026-11-28T00:00:00Z',avatar_url:null,linked_player_ids:[],academy_group_ids:[]});
    let active='parent', otpRequests=0, switches=0;
    await profileContext.addInitScript(()=>{localStorage.setItem('mobile_token','synthetic-parent-token');
      localStorage.setItem('mobile_available_roles','["parent","fan","management"]');});
    await profileContext.route('**/api/mobile/**',async route=>{
      const req=route.request(), endpoint=new URL(req.url()).pathname;
      const send=(status,body)=>route.fulfill({status,contentType:'application/json',headers:{'Cache-Control':'no-store'},body:JSON.stringify(body)});
      if(endpoint.includes('/auth/request-otp')||endpoint.includes('/auth/verify-otp')){otpRequests++;return send(403,{detail:'Unexpected OTP'});}
      if(req.headers().authorization!=='Bearer synthetic-'+active+'-token')return send(401,{code:'invalid_bearer'});
      if(endpoint==='/api/mobile/auth/me'&&req.method()==='GET')return send(200,profiles[active]);
      if(endpoint==='/api/mobile/auth/switch-role'&&req.method()==='POST'){
        const {role}=req.postDataJSON();
        if(!grants.includes(role))return send(403,{detail:'Role unavailable'});
        active=role;switches++;
        return send(200,{token:'synthetic-'+active+'-token',user:profiles[active],role:active,available_roles:grants});
      }
      return send(503,{detail:'No private fixture supplied'});
    });
    const page=await profileContext.newPage();
    await page.goto(origin+'/app?tab=profile',{waitUntil:'domcontentloaded'});
    const profile=page.getByTestId('mobile-own-profile');
    await profile.getByRole('heading',{name:'Synthetic Parent Profile'}).waitFor();
    assert.equal(await page.getByTestId('profile-switch-role-management').count(),0);
    await page.getByTestId('profile-switch-role-fan').click();
    await profile.getByRole('heading',{name:'Synthetic Fan Profile'}).waitFor();
    assert.equal(await profile.getByText('parent@example.test',{exact:true}).count(),0);
    assert.equal(await page.getByTestId('favorite-team-select').inputValue(),'academy');
    assert.equal(new URL(page.url()).searchParams.get('tab'),'profile');
    await page.getByTestId('profile-switch-role-parent').click();
    await profile.getByRole('heading',{name:'Synthetic Parent Profile'}).waitFor();
    assert.equal(await profile.getByText('fan@example.test',{exact:true}).count(),0);
    assert.equal(await page.getByTestId('favorite-team-select').count(),0);
    assert.equal(await page.getByTestId('mobile-app').getAttribute('data-mobile-role'),'parent');
    assert.equal(switches,2);assert.equal(otpRequests,0);
    await page.getByTestId('profile-logout').click();
    await page.getByTestId('mobile-guest-app').waitFor();
    assert.equal(await page.getByTestId('mobile-own-profile').count(),0);
    results.push({kind:'synthetic-profile-role-roundtrip',switches,otpRequests,logout:'guest'});
  } catch(error){failures.push('profile role roundtrip: '+(error.stack||error.message));}
  finally{await profileContext.close();}
  // Real Svelte route/controller with synthetic ballot service responses.
  // This is a browser UX check, not real native voting activation.
  const ballotContext=await browser.newContext({viewport:{width:390,height:844},locale:'el-GR'});
  try {
    const month='2026-11';
    let ballot={month_key:month,voting_open:false,my_vote_player_id:null,revision:0};
    let writes=0,publicReads=0,pickerReads=0,conflictNext=false;
    const candidate=(id,name,extra={})=>({id,name,number:9,position:'Forward',image_url:null,
      organization_id:'lefteria_fc',team_type:'First Team',...extra});
    const publicRoster={organization_id:'lefteria_fc',time_zone:'Asia/Nicosia',
      generated_at:'2026-11-28T11:00:00Z',snapshot_at:'2026-11-27T10:00:00Z',
      players:[candidate('a','Published A'),candidate('b','Published B'),
        candidate('inactive','Inactive Candidate'),candidate('youth','PRIVATE YOUTH SENTINEL',{organization_id:'lefteria_fc_academy',team_type:'Academy'})],
      modules:[{id:'potm',organization_id:'lefteria_fc',status:'empty',quarantined_count:0}],
      potm:{organization_id:'lefteria_fc',month_key:month,total_votes:0,results:[]}};
    const fan={id:'11111111-1111-4111-8111-111111111111',phone:'+35799123456',role:'fan',
      name:'Synthetic Ballot Fan',platform:'Web/PWA',created_at:'2026-01-01T00:00:00Z',
      last_login:'2026-11-28T00:00:00Z',available_roles:['fan'],current_role_available:true,
      avatar_url:null,linked_player_ids:[],academy_group_ids:[]};
    await ballotContext.addInitScript(()=>{localStorage.setItem('mobile_token','synthetic-ballot-token');
      localStorage.setItem('mobile_available_roles','["fan","management"]');});
    await ballotContext.route('**/api/public/sections?*',async route=>{
      publicReads++;
      assert.equal(route.request().headers().authorization,undefined);
      assert.equal(new URL(route.request().url()).searchParams.get('organization_id'),'lefteria_fc');
      return route.fulfill({status:200,contentType:'application/json',body:JSON.stringify(publicRoster)});
    });
    await ballotContext.route('**/api/mobile/**',async route=>{
      const req=route.request(), endpoint=new URL(req.url()).pathname;
      const send=(status,body)=>route.fulfill({status,contentType:'application/json',headers:{'Cache-Control':'private, no-store'},body:JSON.stringify(body)});
      if(req.headers().authorization!=='Bearer synthetic-ballot-token')return send(401,{code:'invalid_bearer'});
      if(endpoint==='/api/mobile/auth/me'&&req.method()==='GET')return send(200,fan);
      if(endpoint==='/api/mobile/players-list'&&req.method()==='GET'){
        pickerReads++;
        return send(200,{players:[{id:'a',name:'PRIVATE CANONICAL SENTINEL'},{id:'b'},{id:'youth'}]});
      }
      if(endpoint==='/api/mobile/potm/ballot'){
        if(req.method()==='GET')return send(200,ballot);
        if(req.method()==='PUT'){
          writes++;
          const body=req.postDataJSON();
          assert.deepEqual(Object.keys(body).sort(),['expected_revision','month_key','player_id']);
          assert.equal(body.month_key,month);
          if(conflictNext){conflictNext=false;ballot={...ballot,my_vote_player_id:'b',revision:2};
            return send(409,{code:'potm_revision_conflict',detail:'Refresh required'});}
          assert.equal(body.expected_revision,ballot.revision);
          ballot={...ballot,my_vote_player_id:body.player_id,revision:ballot.revision+1};
          return send(200,ballot);
        }
      }
      return send(503,{detail:'No private fixture supplied'});
    });
    const page=await ballotContext.newPage();
    await page.goto(origin+'/app?tab=home',{waitUntil:'domcontentloaded'});
    await page.getByTestId('fan-home-potm').click();
    const screen=page.getByTestId('mobile-potm-ballot');
    await screen.getByTestId('mobile-potm-closed').waitFor();
    assert.equal(writes,0);assert.equal(pickerReads,0);assert.equal(publicReads,0);
    assert.equal(await screen.getByTestId('mobile-potm-save').count(),0);
    ballot={...ballot,voting_open:true};
    await screen.getByTestId('mobile-potm-refresh').click();
    await screen.getByTestId('mobile-potm-candidate-a').waitFor();
    assert.equal(await screen.getByTestId('mobile-potm-candidate-inactive').count(),0);
    assert.equal(await screen.getByTestId('mobile-potm-candidate-youth').count(),0);
    assert.doesNotMatch(await screen.textContent(),/PRIVATE.*SENTINEL/);
    await screen.getByTestId('mobile-potm-candidate-a').check();
    await screen.getByTestId('mobile-potm-save').click();
    await screen.getByTestId('mobile-potm-confirmation').waitFor();
    assert.equal(writes,0,'Vote must require explicit confirmation');
    await screen.getByTestId('mobile-potm-confirm').click();
    await screen.getByTestId('mobile-potm-success').waitFor();
    assert.equal(writes,1);assert.equal(ballot.my_vote_player_id,'a');
    await screen.getByTestId('mobile-potm-candidate-b').check();
    await screen.getByTestId('mobile-potm-save').click();
    conflictNext=true;
    await screen.getByTestId('mobile-potm-confirm').click();
    await screen.getByTestId('mobile-potm-error').waitFor();
    assert.equal(writes,2);
    assert.equal(await screen.getByTestId('mobile-potm-save').count(),0,'Conflict must require explicit refresh');
    await screen.getByTestId('mobile-potm-refresh').click();
    await screen.getByTestId('mobile-potm-withdraw').waitFor();
    assert.match(await screen.getByTestId('mobile-potm-current').textContent(),/Published B/);
    await screen.getByTestId('mobile-potm-withdraw').click();
    assert.equal(writes,2);
    await screen.getByTestId('mobile-potm-confirm').click();
    await screen.getByTestId('mobile-potm-success').waitFor();
    assert.equal(ballot.my_vote_player_id,null);assert.equal(ballot.revision,3);assert.equal(writes,3);
    const dimensions=await page.evaluate(()=>[document.documentElement.scrollWidth,document.documentElement.clientWidth]);
    assert.ok(dimensions[0]<=dimensions[1]+2,'Ballot screen overflows phone viewport');
    await page.screenshot({path:path.join(artifacts,'authenticated-fan-potm-phone.png'),fullPage:true,animations:'disabled'});
    results.push({kind:'synthetic-potm-ballot-lifecycle',writes,publicReads,pickerReads,revision:ballot.revision});
  }catch(error){failures.push('POTM ballot lifecycle: '+(error.stack||error.message));}
  finally{await ballotContext.close();}
  await writeFile(path.join(artifacts,'authenticated-role-results.json'),
    JSON.stringify({kind:'real-app-controller-with-synthetic-identity-not-real-backend-auth',
      results,failures},null,2)+'\n');
  console.log(JSON.stringify({actualProtectedRoleRouteCases:results.length,failures}));
  assert.equal(failures.length,0,'Authenticated synthetic /app route parity check failed');
}finally{
  await browser?.close();
  if(server && server.exitCode===null){
    const stopped=once(server,'exit');server.kill('SIGTERM');
    await Promise.race([stopped,delay(3000).then(()=>server.kill('SIGKILL'))]);
  }
}
