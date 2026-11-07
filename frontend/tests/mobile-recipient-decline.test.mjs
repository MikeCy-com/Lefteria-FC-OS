import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';

const fan = {
  id:'11111111-1111-4111-8111-111111111111',
  phone:'+35799123456',role:'fan',name:'Δοκιμαστικός φίλαθλος',
  platform:'Web/PWA',created_at:'2026-11-01T00:00:00Z',
  last_login:'2026-11-07T00:00:00Z',current_role_available:true,
  available_roles:['fan']
};
const id='22222222-2222-4222-8222-222222222222';
const invite=()=>({id,organization_id:'lefteria_fc_academy',status:'pending',
  created_at:new Date(Date.now()-60_000).toISOString(),
  expires_at:new Date(Date.now()+86_400_000).toISOString()});
function setup(decline=()=>Response.json({ok:true}),person=fan) {
  const calls=[];
  const controller=createMobileAuthController({
    baseUrl:'https://club.invalid',
    storage:createMobileSessionStorage({
      getItem:key=>key==='mobile_token'?'otp-fan-session':null,
      setItem:()=>{},removeItem:()=>{}
    }),
    fetch:async request=>{
      const entry={path:new URL(request.url).pathname,method:request.method,
        authorization:request.headers.get('Authorization')};
      calls.push(entry);
      if(entry.path==='/api/mobile/auth/me')return Response.json(person);
      if(entry.path==='/api/mobile/family/invitations' && entry.method==='GET')
        return Response.json({items:[invite()]});
      if(entry.path==='/api/mobile/family/invitations/'+id+'/decline' &&
        entry.method==='POST')return await decline();
      throw Error('Unexpected endpoint '+entry.path);
    }
  });
  return {controller,calls};
}

test('verified fan can decline only their currently visible pending reservation',async()=>{
  const {controller,calls}=setup();
  await controller.bootstrap();
  assert.equal(await controller.declineRecipientInvitation(id),false);
  assert.equal(await controller.loadRecipientInvitations(),true);
  assert.equal(await controller.declineRecipientInvitation('33333333-3333-4333-8333-333333333333'),false);
  assert.equal(await controller.declineRecipientInvitation('incorrect'),false);
  assert.equal(await controller.declineRecipientInvitation(id),true);
  assert.equal(controller.getState().recipientInvitations.items.length,0);
  assert.equal(controller.getState().recipientDecliningId,null);
  assert.equal(controller.getState().recipientDeclineError,null);
  assert.deepEqual(calls.filter(x=>x.method==='POST'),[{
    path:'/api/mobile/family/invitations/'+id+'/decline',
    method:'POST',authorization:'Bearer otp-fan-session'
  }]);
  assert.equal(await controller.declineRecipientInvitation(id),false);
  assert.equal(calls.filter(x=>x.method==='POST').length,1);
});

test('rate limits or invalid confirmations never optimistically remove a pending reservation',async()=>{
  for(const response of [
    ()=>Response.json({detail:'Too many requests',code:'rate_limited'},{status:429}),
    ()=>Response.json({ok:false})
  ]) {
    const {controller}=setup(response);
    await controller.bootstrap();
    assert.equal(await controller.loadRecipientInvitations(),true);
    assert.equal(await controller.declineRecipientInvitation(id),false);
    assert.equal(controller.getState().recipientInvitations.items.length,1);
    assert.equal(controller.getState().recipientDecliningId,null);
    assert.ok(controller.getState().recipientDeclineError);
  }
});

test('revoked or cancelled reservation resets private inbox; no stale card can remain',async()=>{
  const {controller}=setup(()=>Response.json({detail:'Invitation not found',code:'invalid_input'},
    {status:404}));
  await controller.bootstrap();
  assert.equal(await controller.loadRecipientInvitations(),true);
  assert.equal(await controller.declineRecipientInvitation(id),false);
  assert.equal(controller.getState().recipientInvitations,null);
  assert.equal(controller.getState().recipientStatus,'idle');
  assert.ok(controller.getState().recipientDeclineError);
});

test('wrong role and logout during decline cannot mutate the local verified inbox',async()=>{
  const wrong=setup(()=>Response.json({ok:true}),{...fan,role:'parent',available_roles:['parent','fan']});
  await wrong.controller.bootstrap();
  assert.equal(await wrong.controller.declineRecipientInvitation(id),false);
  assert.equal(wrong.calls.filter(x=>x.method==='POST').length,0);

  const pending=Promise.withResolvers();
  const {controller}=setup(()=>pending.promise);
  await controller.bootstrap();
  assert.equal(await controller.loadRecipientInvitations(),true);
  const action=controller.declineRecipientInvitation(id);
  assert.equal(controller.getState().recipientDecliningId,id);
  controller.logout();
  pending.resolve(Response.json({ok:true}));
  assert.equal(await action,false);
  assert.equal(controller.getState().recipientInvitations,null);
  assert.equal(controller.getState().recipientDecliningId,null);
  assert.equal(controller.getState().status,'anonymous');
});

registerHooks({
  resolve(specifier,context,nextResolve) {
    if(specifier.endsWith('.css'))
      return {url:'data:text/javascript,export%20default%20%7B%7D',shortCircuit:true};
    return nextResolve(specifier,context);
  },
  load(url,context,nextLoad) {
    if(url.endsWith('.svelte')){
      const filename=fileURLToPath(url);
      return {format:'module',source:compile(readFileSync(filename,'utf8'),
        {filename,generate:'server'}).js.code,shortCircuit:true};
    }
    return nextLoad(url,context);
  }
});

test('recipient invite SSR requires explicit decline callback and never shows an accept control',async()=>{
  const {default:Component}=await import('../src/lib/mobile/components/MobileRecipientInvitations.svelte');
  const props={inbox:{items:[invite()]},status:'ready',error:null,busy:false,onLoad:()=>true};
  const basic=render(Component,{props}).body;
  assert.doesNotMatch(basic,/data-testid="recipient-decline-start"/);
  const ready=render(Component,{props:{...props,
    onDecline:()=>true,decliningId:null,declineError:null}}).body;
  assert.match(ready,/data-testid="recipient-decline-start"/);
  assert.match(ready,/Απόρριψη καταχώρισης/);
  assert.match(ready,/Δεν έχει αποσταλεί SMS/);
  assert.match(ready,/Under Developments/);
  assert.doesNotMatch(ready,/target_phone|child_name|accept-invitation|recipient-accept-submit/);
  const failed=render(Component,{props:{...props,
    onDecline:()=>false,declineError:'Η πρόσκληση δεν είναι διαθέσιμη'}}).body;
  assert.match(failed,/data-testid="recipient-decline-error"/);
  const src=readFileSync(new URL('../src/lib/mobile/components/MobileRecipientInvitations.svelte',import.meta.url),'utf8');
  assert.match(src,/data-testid="recipient-decline-confirm"/);
  assert.match(src,/data-testid="recipient-decline-submit"/);
  const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  assert.match(route,/onDecline=\{\(id\) => controller\.declineRecipientInvitation\(id\)\}/);
  assert.match(route,/activeTab === 'profile' && role === 'fan' && !composing/);
});
