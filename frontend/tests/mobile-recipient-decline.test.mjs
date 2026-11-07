import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';

const id='22222222-2222-4222-8222-222222222222';
const person={id:'11111111-1111-4111-8111-111111111111',phone:'+35799123456',
  role:'fan',name:'Συνθετικός φίλαθλος',available_roles:['fan'],
  current_role_available:true,platform:'Web/PWA',
  created_at:'2026-01-01T00:00:00Z',last_login:'2026-11-07T00:00:00Z'};
const inbox=()=>({items:[{id,organization_id:'lefteria_fc_academy',
  status:'pending',created_at:new Date(Date.now()-60000).toISOString(),
  expires_at:new Date(Date.now()+86400000).toISOString()}]});
function setup(decline=()=>Response.json({ok:true}),user=person){
  const requests=[];
  const storage=createMobileSessionStorage({
    getItem:k=>k==='mobile_token'?'synthetic-fan-otp':null,
    setItem:()=>{},removeItem:()=>{}
  });
  const controller=createMobileAuthController({
    baseUrl:'https://club.invalid',storage,
    fetch:async req=>{
      const url=new URL(req.url),row={path:url.pathname,method:req.method,
        authorization:req.headers.get('Authorization')};requests.push(row);
      if(url.pathname==='/api/mobile/auth/me')return Response.json(user);
      if(url.pathname==='/api/mobile/family/invitations')return Response.json(inbox());
      if(url.pathname==='/api/mobile/family/invitations/'+id+'/decline')return await decline(row);
      throw Error('Unexpected request '+url.pathname);
    }
  });return{controller,requests};
}

test('only the current fan inbox invitation can be declined and removed after success',async()=>{
  const {controller,requests}=setup();
  await controller.bootstrap();
  assert.equal(await controller.declineRecipientInvitation(id),false);
  assert.equal(await controller.loadRecipientInvitations(),true);
  assert.equal(await controller.declineRecipientInvitation('33333333-3333-4333-8333-333333333333'),false);
  assert.equal(await controller.declineRecipientInvitation(id),true);
  assert.deepEqual(controller.getState().recipientInvitations.items,[]);
  assert.equal(controller.getState().recipientDecliningId,null);
  assert.equal(controller.getState().recipientDeclineError,null);
  assert.deepEqual(requests.filter(x=>x.method==='POST'), [{
    path:'/api/mobile/family/invitations/'+id+'/decline',method:'POST',
    authorization:'Bearer synthetic-fan-otp'
  }]);
  assert.equal(await controller.declineRecipientInvitation(id),false);
  assert.equal(requests.filter(x=>x.method==='POST').length,1);
});

test('parent or other non-fan identity cannot send a decline request',async()=>{
  for(const role of ['parent','player','coach','management']){
    const {controller,requests}=setup(()=>Response.json({ok:true}),
      {...person,role,available_roles:[role,'fan']});
    await controller.bootstrap();
    assert.equal(await controller.declineRecipientInvitation(id),false);
    assert.equal(await controller.loadRecipientInvitations(),false);
    assert.equal(requests.filter(x=>x.method==='POST').length,0);
  }
});

test('decline failure does not lie about success or clear the reservation',async()=>{
  const {controller}=setup(()=>Response.json({detail:'Rate limited',
    code:'rate_limited'},{status:429}));
  await controller.bootstrap();
  assert.equal(await controller.loadRecipientInvitations(),true);
  assert.equal(await controller.declineRecipientInvitation(id),false);
  assert.equal(controller.getState().recipientInvitations.items.length,1);
  assert.equal(controller.getState().recipientDecliningId,null);
  assert.ok(controller.getState().recipientDeclineError);
});

test('stale reservation response clears the private inbox; logout prevents late success',async()=>{
  const denied=setup(()=>Response.json({detail:'Not found',code:'invalid_input'},{status:404}));
  await denied.controller.bootstrap();
  assert.equal(await denied.controller.loadRecipientInvitations(),true);
  assert.equal(await denied.controller.declineRecipientInvitation(id),false);
  assert.equal(denied.controller.getState().recipientInvitations,null);
  assert.equal(denied.controller.getState().recipientStatus,'idle');

  const delayed=Promise.withResolvers();
  const {controller}=setup(()=>delayed.promise);
  await controller.bootstrap();
  assert.equal(await controller.loadRecipientInvitations(),true);
  const mutation=controller.declineRecipientInvitation(id);
  assert.equal(controller.getState().recipientDecliningId,id);
  controller.logout();
  delayed.resolve(Response.json({ok:true}));
  assert.equal(await mutation,false);
  assert.equal(controller.getState().status,'anonymous');
  assert.equal(controller.getState().recipientInvitations,null);
});

registerHooks({
  resolve(specifier,context,nextResolve){
    if(specifier.endsWith('.css'))return{url:'data:text/javascript,export%20default%20%7B%7D',shortCircuit:true};
    return nextResolve(specifier,context);
  },
  load(url,context,nextLoad){
    if(url.endsWith('.svelte')){
      const filename=fileURLToPath(url);
      return{format:'module',source:compile(readFileSync(filename,'utf8'),
        {filename,generate:'server'}).js.code,shortCircuit:true};
    }
    return nextLoad(url,context);
  }
});

test('recipient Svelte UI has decline affordance but no accept, SMS or child identifier',async()=>{
  const {default:Inbox}=await import('../src/lib/mobile/components/MobileRecipientInvitations.svelte');
  const props={inbox:inbox(),status:'ready',error:null,busy:false,onLoad:()=>true};
  const readOnly=render(Inbox,{props}).body;
  assert.doesNotMatch(readOnly,/data-testid="recipient-decline-start"/);
  const html=render(Inbox,{props:{...props,onDecline:()=>true}}).body;
  assert.match(html,/data-testid="recipient-decline-start"/);
  assert.match(html,/Απόρριψη καταχώρισης/);
  assert.match(html,/Δεν έχει αποσταλεί SMS/);
  assert.doesNotMatch(html,/recipient-accept-submit|player_id|child_name|parent_phone|target_phone/);
  const component=readFileSync(new URL('../src/lib/mobile/components/MobileRecipientInvitations.svelte',import.meta.url),'utf8');
  assert.match(component,/data-testid="recipient-decline-confirm"/);
  assert.match(component,/data-testid="recipient-decline-submit"/);
  const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  assert.match(route,/onDecline=\{\(id\) => controller\.declineRecipientInvitation\(id\)\}/);
  assert.match(route,/activeTab === 'profile' && role === 'fan' && !composing/);
});
