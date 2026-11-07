import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';

const identityId='11111111-1111-4111-8111-111111111111';
const invitationId='22222222-2222-4222-8222-222222222222';
const user={id:identityId,role:'parent',phone:'+35799123456',name:'Δοκιμαστικός γονέας',
  current_role_available:true,available_roles:['parent','fan'],platform:'Web/PWA',
  created_at:'2026-01-01T00:00:00Z',last_login:'2026-11-07T00:00:00Z'};
const family={children:[{id:'child-1',name:'Παιδί Άλφα',organization_id:'lefteria_fc_academy',
  team_type:'Academy',team_id:null,academy_group_ids:['academy-group']}]};
const outbox={items:[{id:invitationId,player_id:'child-1',organization_id:'lefteria_fc_academy',
  status:'pending',created_at:new Date(Date.now()-30000).toISOString(),
  expires_at:new Date(Date.now()+86400000).toISOString()}]};

function createFixture(cancel) {
  const calls=[];
  const storage=createMobileSessionStorage({
    getItem:k=>k==='mobile_token'?'parent-fixture-token':null,
    setItem:()=>{}, removeItem:()=>{}
  });
  const controller=createMobileAuthController({
    baseUrl:'https://club.example', storage,
    fetch:async request=>{
      const row={path:new URL(request.url).pathname,method:request.method,
        authorization:request.headers.get('Authorization')};
      calls.push(row);
      if(row.path==='/api/mobile/auth/me')return Response.json(user);
      if(row.path==='/api/mobile/family')return Response.json(family);
      if(row.path==='/api/mobile/family/outgoing-invitations')return Response.json(outbox);
      if(row.path==='/api/mobile/family/active-children')return Response.json({items:[{player_id:'child-1',organization_id:'lefteria_fc_academy'}]});
      if(row.path==='/api/mobile/family/invitations/'+invitationId+'/cancel')
        return await cancel(row);
      if(row.path==='/api/mobile/family/children/child-1/revoke')
        return Response.json({ok:true});
      throw Error('Unexpected test-only request '+row.path);
    }
  });
  return {controller,calls};
}

test('only an already verified pending invitation can be cancelled by current parent',async()=>{
  const {controller,calls}=createFixture(()=>Response.json({ok:true}));
  await controller.bootstrap();
  assert.equal(await controller.cancelOutgoingInvitation(invitationId),false);
  assert.equal(await controller.loadFamily(),true);
  assert.equal(await controller.loadOutgoingInvitations(),true);
  assert.equal(await controller.cancelOutgoingInvitation('33333333-3333-4333-8333-333333333333'),false);
  assert.equal(await controller.cancelOutgoingInvitation(invitationId),true);
  assert.equal(controller.getState().outgoingInvitations.items.length,0);
  assert.equal(controller.getState().outgoingCancelError,null);
  assert.equal(controller.getState().outgoingCancellingId,null);
  const mutationCalls=calls.filter(x=>x.method==='POST');
  assert.deepEqual(mutationCalls,[{
    path:'/api/mobile/family/invitations/'+invitationId+'/cancel',
    method:'POST',authorization:'Bearer parent-fixture-token'
  }]);
  assert.equal(await controller.cancelOutgoingInvitation(invitationId),false);
  assert.equal(calls.filter(x=>x.method==='POST').length,1);
});

test('parent revocation uses a canonical child and rechecks authorization',async()=>{
  const {controller,calls}=createFixture(()=>Response.json({ok:true}));
  await controller.bootstrap();
  assert.equal(await controller.revokeFamilyDelegation('child-1'),false);
  assert.equal(await controller.loadFamily(),true);
  assert.equal(await controller.revokeFamilyDelegation('child-1'),false);
  assert.equal(await controller.loadActiveFamilyDelegations(),true);
  assert.equal(await controller.revokeFamilyDelegation('unknown-child'),false);
  assert.equal(await controller.revokeFamilyDelegation('child-1'),true);
  assert.equal(controller.getState().familyRevokeSuccess,'Η οικογενειακή πρόσβαση ανακλήθηκε.');
  assert.equal(controller.getState().activeFamilyDelegations.items.length,0);
  assert.equal(await controller.revokeFamilyDelegation('child-1'),false);
  const actions=calls.filter(x=>x.method==='POST');
  assert.deepEqual(actions,[{path:'/api/mobile/family/children/child-1/revoke',
    method:'POST',authorization:'Bearer parent-fixture-token'}]);
  controller.logout();
  assert.equal(controller.getState().familyRevokeSuccess,null);
});

test('cancel failure preserves pending record and displays error without claiming success',async()=>{
  const {controller}=createFixture(()=>Response.json({detail:'Rate limited',code:'rate_limited'},
    {status:429}));
  await controller.bootstrap();
  assert.equal(await controller.loadFamily(),true);
  assert.equal(await controller.loadOutgoingInvitations(),true);
  assert.equal(await controller.cancelOutgoingInvitation(invitationId),false);
  assert.equal(controller.getState().outgoingInvitations.items.length,1);
  assert.equal(controller.getState().outgoingCancellingId,null);
  assert.ok(controller.getState().outgoingCancelError);
});

test('revoked guardian cancellation response invalidates cached children and outbox',async()=>{
  const {controller}=createFixture(()=>Response.json({detail:'Invitation not found',code:'invalid_input'},
    {status:404}));
  await controller.bootstrap();
  assert.equal(await controller.loadFamily(),true);
  assert.equal(await controller.loadOutgoingInvitations(),true);
  assert.equal(await controller.cancelOutgoingInvitation(invitationId),false);
  assert.equal(controller.getState().family,null);
  assert.equal(controller.getState().outgoingInvitations,null);
  assert.equal(controller.getState().outgoingStatus,'idle');
  assert.ok(controller.getState().outgoingCancelError);
});

test('late cancellation result after logout never restores invitation data',async()=>{
  const delayed=Promise.withResolvers();
  const {controller}=createFixture(()=>delayed.promise);
  await controller.bootstrap();
  assert.equal(await controller.loadFamily(),true);
  assert.equal(await controller.loadOutgoingInvitations(),true);
  const mutation=controller.cancelOutgoingInvitation(invitationId);
  assert.equal(controller.getState().outgoingCancellingId,invitationId);
  controller.logout();
  delayed.resolve(Response.json({ok:true}));
  assert.equal(await mutation,false);
  assert.equal(controller.getState().outgoingInvitations,null);
  assert.equal(controller.getState().status,'anonymous');
});

registerHooks({
  resolve(specifier,context,nextResolve){
    if(specifier.endsWith('.css'))return {url:'data:text/javascript,export%20default%20%7B%7D',
      shortCircuit:true};
    return nextResolve(specifier,context);
  },
  load(url,context,nextLoad){
    if(url.endsWith('.svelte')){
      const filename=fileURLToPath(url);
      return {format:'module',source:compile(readFileSync(filename,'utf8'),
        {filename,generate:'server'}).js.code,shortCircuit:true};
    }
    return nextLoad(url,context);
  }
});

test('Svelte pending card only offers cancellation when explicitly enabled; no fake access activation',async()=>{
  const {default:MobileFamily}=await import('../src/lib/mobile/components/MobileFamily.svelte');
  const props={family,status:'ready',error:null,busy:false,onLoad:()=>true,onCalendar:()=>{},
    outgoingInvitations:outbox,outgoingStatus:'ready',outgoingError:null,
    onLoadOutgoing:()=>true};
  const readOnly=render(MobileFamily,{props}).body;
  assert.doesNotMatch(readOnly,/data-testid="family-cancel-start"/);
  const ready=render(MobileFamily,{props:{...props,
    onCancelOutgoing:async()=>true,outgoingCancellingId:null,
    outgoingCancelError:null}}).body;
  assert.match(ready,/data-testid="family-cancel-start"/);
  assert.match(ready,/Ακύρωση καταχώρισης/);
  assert.match(ready,/Δεν έχει αποσταλεί πρόσκληση ή SMS/);
  assert.doesNotMatch(ready,/target_phone|guardian_phone|accept-invitation|send-sms/);
  const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  const component=readFileSync(new URL('../src/lib/mobile/components/MobileFamily.svelte',import.meta.url),'utf8');
  assert.match(route,/onCancelOutgoing=\{\(id\) => controller\.cancelOutgoingInvitation\(id\)\}/);
  assert.match(component,/data-testid="family-cancel-confirm"/);
  assert.match(component,/data-testid="family-cancel-submit"/);
  const revokable=render(MobileFamily,{props:{...props,onRevokeDelegation:async()=>true,
    activeStatus:'ready',activeDelegations:{items:[{player_id:'child-1',organization_id:'lefteria_fc_academy'}]}}}).body;
  const noGrants=render(MobileFamily,{props:{...props,onRevokeDelegation:async()=>true,
    activeStatus:'ready',activeDelegations:{items:[]}}}).body;
  assert.doesNotMatch(noGrants,/family-revoke-start/);
  assert.match(revokable,/family-revoke-start/);
  assert.doesNotMatch(readOnly,/family-revoke-start/);
  assert.ok(route.includes('onRevokeDelegation={(id) => controller.revokeFamilyDelegation(id)}'));
});
