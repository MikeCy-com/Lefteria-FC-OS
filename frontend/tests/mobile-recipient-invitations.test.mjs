import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { registerHooks } from 'node:module';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';
import { createMobileAuthController } from '../src/lib/mobile/controller.ts';
import { createMobileSessionStorage } from '../src/lib/mobile/session.ts';

const user = {
  id:'11111111-1111-4111-8111-111111111111',
  phone:'+35799123456', role:'fan', name:'Συνθετικός φίλαθλος',
  platform:'Web/PWA', created_at:'2026-11-01T00:00:00Z',
  last_login:'2026-11-07T00:00:00Z',
  current_role_available:true, available_roles:['fan']
};
const id = '22222222-2222-4222-8222-222222222222';
function invitation(overrides={}) {
  return {id,organization_id:'lefteria_fc_academy',status:'pending',
    created_at:new Date(Date.now()-60_000).toISOString(),
    expires_at:new Date(Date.now()+86_400_000).toISOString(),...overrides};
}
function calendarPayload(overrides={}) {
  const child='academy-child-id';
  const organization='lefteria_fc_academy';
  return {identity_id:user.id,role:'fan',month:'2026-11',time_zone:'Asia/Nicosia',
    organization_id:organization,selected_player_id:child,
    players:[{id:child,name:'Αθλητής δοκιμής',organization_id:organization}],
    items:[{id:'training-1',kind:'training',event_type:'training',organization_id:organization,
      title:'Προπόνηση Ακαδημίας',date:'2026-11-08',end_date:'2026-11-08',
      start_time:'17:00',location:'Γήπεδο',is_cancelled:false,
      member_summary:'Προπόνηση ομάδας',content_status:null,responses:[{player_id:child,status:'going'}]}],
    next_offset:null,...overrides};
}
function setup(person=user, serve=()=>Response.json({items:[invitation()]}),
  accept=()=>Response.json({ok:true}), delegation=()=>Response.json({has_active_delegation:false}),
  calendar=()=>Response.json(calendarPayload()),
  rsvp=()=>Response.json({event_id:'training-1',event_type:'training',player_id:'academy-child-id',status:'maybe'})) {
  const requests=[];
  const storage=createMobileSessionStorage({
    getItem:key=>key==='mobile_token'?'synthetic-otp-session':null,
    setItem:()=>{},removeItem:()=>{}
  });
  const controller=createMobileAuthController({
    baseUrl:'https://club.invalid',storage,
    fetch:async request=>{
      const path=new URL(request.url).pathname;
      requests.push({path,method:request.method,authorization:request.headers.get('Authorization')});
      if(path==='/api/mobile/auth/me')return Response.json(person);
      if(path==='/api/mobile/family/invitations')return await serve();
      if(path==='/api/mobile/family/active-delegation')return await delegation();
      if(path==='/api/mobile/family/calendar')return await calendar();
      if(path==='/api/mobile/family/availability'){
        requests.at(-1).body=await request.clone().json();
        return await rsvp();
      }
      if(path==='/api/mobile/family/invitations/'+id+'/accept')return await accept();
      throw new Error('Unexpected endpoint '+path);
    }
  });
  return {controller,requests};
}

test('only a verified current fan can fetch private recipient metadata, projected without child fields', async()=>{
  const payload={items:[invitation({
    player_id:'secret-child-id',child_name:'SECRET CHILD',
    target_phone:'35799999999',parent_phone:'35788888888',
    audit_events:[{action:'test-private'}]
  })]};
  const {controller,requests}=setup(user,()=>Response.json(payload));
  await controller.bootstrap();
  assert.equal(await controller.loadRecipientInvitations(),true);
  assert.equal(controller.getState().recipientStatus,'ready');
  assert.deepEqual(requests.at(-1),{
    path:'/api/mobile/family/invitations',method:'GET',
    authorization:'Bearer synthetic-otp-session'
  });
  const kept=JSON.stringify(controller.getState().recipientInvitations);
  assert.ok(kept.includes('lefteria_fc_academy'));
  assert.doesNotMatch(kept,/SECRET CHILD|secret-child-id|99999999|88888888|audit_events|target_phone/);
  assert.deepEqual(Object.keys(controller.getState().recipientInvitations.items[0]),
    ['id','organization_id','status','created_at','expires_at']);
  controller.logout();
  assert.equal(controller.getState().status,'anonymous');
  assert.equal(controller.getState().recipientInvitations,null);
  assert.equal(controller.getState().recipientStatus,'idle');
});

test('verified fan reads only the Boolean delegation status and clears it on logout',async()=>{
  const {controller,requests}=setup();
  await controller.bootstrap();
  assert.equal(await controller.loadDelegationAvailability(),true);
  assert.deepEqual(controller.getState().delegationAvailability,{has_active_delegation:false});
  assert.equal(controller.getState().delegationStatus,'ready');
  assert.deepEqual(requests.at(-1),{path:'/api/mobile/family/active-delegation',method:'GET',authorization:'Bearer synthetic-otp-session'});
  controller.logout();
  assert.equal(controller.getState().delegationAvailability,null);
  assert.equal(controller.getState().delegationStatus,'idle');
});

test('delegated fan calendar requires server-verified live grant and uses only member projection',async()=>{
  const {controller,requests}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:true}));
  await controller.bootstrap();
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),false);
  assert.equal(await controller.loadDelegationAvailability(),true);
  assert.equal(await controller.loadDelegatedCalendar('bad-month'),false);
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),true);
  const data=controller.getState().delegatedCalendar;
  assert.equal(data.role,'fan');
  assert.equal(data.players.length,1);
  assert.equal(data.items[0].content_status,null);
  assert.deepEqual(data.items[0].responses,[{player_id:'academy-child-id',status:'going'}]);
  assert.ok(requests.some(x=>x.path==='/api/mobile/family/calendar'&&
    x.authorization==='Bearer synthetic-otp-session'&&x.method==='GET'));
  assert.equal(await controller.loadDelegationAvailability(),true);
  controller.logout();
  assert.equal(controller.getState().delegatedCalendar,null);
  assert.equal(controller.getState().delegatedCalendarStatus,'idle');
});

test('refreshing recipient grant status immediately clears stale child calendar events',async()=>{
  let active=true;
  const {controller}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:active}));
  await controller.bootstrap();
  await controller.loadDelegationAvailability();
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),true);
  active=false;
  await controller.loadDelegationAvailability();
  assert.equal(controller.getState().delegatedCalendar,null);
  assert.equal(controller.getState().delegatedCalendarStatus,'idle');
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),false);
});

test('verified fan submits only the sole current child RSVP and reloads server state',async()=>{
  let current='going';
  const {controller,requests}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:true}),
    ()=>Response.json(calendarPayload({items:[{
      ...calendarPayload().items[0],responses:[{player_id:'academy-child-id',status:current}]
    }]})),
    ()=>{current='maybe';return Response.json({event_id:'training-1',event_type:'training',
      player_id:'academy-child-id',status:'maybe'});});
  await controller.bootstrap();
  assert.equal(await controller.loadDelegationAvailability(),true);
  const candidate=calendarPayload().items[0];
  assert.equal(await controller.submitDelegatedAvailability(candidate,'maybe'),false);
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),true);
  assert.equal(await controller.submitDelegatedAvailability({...candidate,id:'unrelated'},'maybe'),false);
  assert.equal(await controller.submitDelegatedAvailability(candidate,'invalid'),false);
  assert.equal(await controller.submitDelegatedAvailability(candidate,'maybe'),true);
  assert.equal(controller.getState().delegatedCalendar.items[0].responses[0].status,'maybe');
  assert.match(controller.getState().delegatedRespondSuccess,/αποθηκεύτηκε/);
  assert.deepEqual(requests.filter(x=>x.method==='POST'),[{
    path:'/api/mobile/family/availability',method:'POST',
    authorization:'Bearer synthetic-otp-session',
    body:{event_id:'training-1',event_type:'training',status:'maybe'}
  }]);
  controller.logout();
  assert.equal(controller.getState().delegatedCalendar,null);
  assert.equal(controller.getState().delegatedRespondSuccess,null);
});

test('delegated RSVP completion after logout cannot restore player information',async()=>{
  const delayed=Promise.withResolvers();
  const {controller}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:true}),undefined,()=>delayed.promise);
  await controller.bootstrap();
  await controller.loadDelegationAvailability();
  await controller.loadDelegatedCalendar('2026-11');
  const pending=controller.submitDelegatedAvailability(calendarPayload().items[0],'maybe');
  assert.equal(controller.getState().delegatedRespondingKey,'training:training-1');
  controller.logout();
  delayed.resolve(Response.json({event_id:'training-1',event_type:'training',
    player_id:'academy-child-id',status:'maybe'}));
  assert.equal(await pending,false);
  assert.equal(controller.getState().status,'anonymous');
  assert.equal(controller.getState().delegatedCalendar,null);
  assert.equal(controller.getState().delegatedRespondSuccess,null);
});

test('revoked delegated RSVP denies without retaining the child calendar',async()=>{
  const {controller}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:true}),undefined,
    ()=>Response.json({code:'invalid_calendar_input',detail:'Grant revoked'},{status:403}));
  await controller.bootstrap();
  await controller.loadDelegationAvailability();
  await controller.loadDelegatedCalendar('2026-11');
  assert.equal(await controller.submitDelegatedAvailability(calendarPayload().items[0],'maybe'),false);
  assert.equal(controller.getState().delegatedCalendar,null);
  assert.equal(controller.getState().delegationAvailability,null);
  assert.equal(controller.getState().delegationStatus,'error');
});

test('delegated calendar denies another player and clears revoked data immediately',async()=>{
  let revoked=false;
  const {controller}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:true}),
    ()=>revoked ? Response.json({detail:'Grant revoked',code:'invalid_calendar_input'},{status:403})
      :Response.json(calendarPayload()));
  await controller.bootstrap();
  await controller.loadDelegationAvailability();
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),true);
  revoked=true;
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),false);
  assert.equal(controller.getState().delegatedCalendar,null);
  assert.equal(controller.getState().delegationAvailability,null);
  assert.equal(controller.getState().delegationStatus,'error');
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),false);
});

test('late delegated calendar response after logout never restores private child events',async()=>{
  const delayed=Promise.withResolvers();
  const {controller}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:true}),()=>delayed.promise);
  await controller.bootstrap();
  await controller.loadDelegationAvailability();
  const pending=controller.loadDelegatedCalendar('2026-11');
  controller.logout();
  delayed.resolve(Response.json(calendarPayload()));
  assert.equal(await pending,false);
  assert.equal(controller.getState().delegatedCalendar,null);
});

test('delegated calendar rejects cross-club events from a faulty response',async()=>{
  const {controller}=setup(user,undefined,undefined,
    ()=>Response.json({has_active_delegation:true}),
    ()=>Response.json(calendarPayload({items:[{
      ...calendarPayload().items[0],organization_id:'lefteria_fc'
    }]})));
  await controller.bootstrap();
  await controller.loadDelegationAvailability();
  assert.equal(await controller.loadDelegatedCalendar('2026-11'),false);
  assert.equal(controller.getState().delegatedCalendar,null);
  assert.equal(controller.getState().delegatedCalendarStatus,'error');
});

test('formal roles cannot read recipient delegation status',async()=>{
  for(const role of ['parent','player','coach','management']){
    const {controller,requests}=setup({...user,role,available_roles:[role,'fan']});
    await controller.bootstrap();
    assert.equal(await controller.loadDelegationAvailability(),false);
    assert.ok(!requests.some(x=>x.path==='/api/mobile/family/active-delegation'));
  }
});

test('verified fan accepts only current pending invitation and cannot replay it',async()=>{
  const {controller,requests}=setup();
  await controller.bootstrap();
  assert.equal(await controller.acceptRecipientInvitation(id),false);
  assert.equal(await controller.loadRecipientInvitations(),true);
  assert.equal(await controller.acceptRecipientInvitation('33333333-3333-4333-8333-333333333333'),false);
  assert.equal(await controller.acceptRecipientInvitation(id),true);
  assert.deepEqual(controller.getState().recipientInvitations.items,[]);
  assert.match(controller.getState().recipientAcceptSuccess,/Ελέγχουμε την πρόσβαση στο πρόγραμμα/);
  assert.equal(await controller.acceptRecipientInvitation(id),false);
  assert.deepEqual(requests.filter(x=>x.method==='POST'),[{
    path:'/api/mobile/family/invitations/'+id+'/accept',method:'POST',
    authorization:'Bearer synthetic-otp-session'
  }]);
  controller.logout();
  assert.equal(controller.getState().recipientAcceptSuccess,null);
});

test('failed acceptance preserves pending invitation and never reports success',async()=>{
  const {controller}=setup(user,()=>Response.json({items:[invitation()]}),
    ()=>Response.json({detail:'Invitation revoked',code:'invalid_input'},{status:404}));
  await controller.bootstrap();
  assert.equal(await controller.loadRecipientInvitations(),true);
  assert.equal(await controller.acceptRecipientInvitation(id),false);
  assert.equal(controller.getState().recipientInvitations,null);
  assert.equal(controller.getState().recipientAcceptSuccess,null);
  assert.ok(controller.getState().recipientAcceptError);
});

test('late acceptance response after logout cannot restore pending or grant data',async()=>{
  const deferred=Promise.withResolvers();
  const {controller}=setup(user,()=>Response.json({items:[invitation()]}),()=>deferred.promise);
  await controller.bootstrap();
  assert.equal(await controller.loadRecipientInvitations(),true);
  const accepting=controller.acceptRecipientInvitation(id);
  assert.equal(controller.getState().recipientAcceptingId,id);
  controller.logout();
  deferred.resolve(Response.json({ok:true}));
  assert.equal(await accepting,false);
  assert.equal(controller.getState().status,'anonymous');
  assert.equal(controller.getState().recipientInvitations,null);
  assert.equal(controller.getState().recipientAcceptSuccess,null);
});

test('formal parent/player/coach roles cannot query the recipient inbox',async()=>{
  for(const role of ['parent','player','coach','management']){
    const {controller,requests}=setup({...user,role,available_roles:[role,'fan']});
    await controller.bootstrap();
    assert.equal(await controller.loadRecipientInvitations(),false);
    assert.ok(!requests.some(request => request.path === '/api/mobile/family/invitations'),
      'Non-fan role called the recipient inbox');
  }
});

test('malformed, expired and multiple recipient records fail closed',async()=>{
  for(const payload of [
    {items:[invitation({organization_id:'other_club'})]},
    {items:[invitation({status:'accepted'})]},
    {items:[invitation({id:'bad-id'})]},
    {items:[invitation({created_at:'not-a-date'})]},
    {items:[invitation({expires_at:new Date(Date.now()-1000).toISOString()})]},
    {items:[invitation(),invitation({id:'33333333-3333-4333-8333-333333333333'})]},
    {items:null},
    {}
  ]){
    const {controller}=setup(user,()=>Response.json(payload));
    await controller.bootstrap();
    assert.equal(await controller.loadRecipientInvitations(),false);
    assert.equal(controller.getState().recipientInvitations,null);
    assert.equal(controller.getState().recipientStatus,'error');
  }
});

test('role revocation, unauthorized response and logout erase private inbox',async()=>{
  const unauthorized=setup(user,()=>Response.json({detail:'Session expired',code:'session_invalid'},{status:401}));
  await unauthorized.controller.bootstrap();
  assert.equal(await unauthorized.controller.loadRecipientInvitations(),false);
  assert.equal(unauthorized.controller.getState().status,'anonymous');
  assert.equal(unauthorized.controller.getState().recipientInvitations,null);

  const revoked=setup(user,()=>Response.json({detail:'Access removed',code:'account_unavailable'},{status:403}));
  await revoked.controller.bootstrap();
  assert.equal(await revoked.controller.loadRecipientInvitations(),false);
  assert.equal(revoked.controller.getState().user.current_role_available,false);
  assert.equal(revoked.controller.getState().recipientInvitations,null);

  const delayed=Promise.withResolvers();
  const pending=setup(user,()=>delayed.promise);
  await pending.controller.bootstrap();
  const loading=pending.controller.loadRecipientInvitations();
  assert.equal(pending.controller.getState().recipientStatus,'loading');
  pending.controller.logout();
  delayed.resolve(Response.json({items:[invitation()]}));
  assert.equal(await loading,false);
  assert.equal(pending.controller.getState().recipientInvitations,null);
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

test('fan inbox Svelte SSR shows only organization and expiry, never target/child or an acceptance action',async()=>{
  const {default:Inbox}=await import('../src/lib/mobile/components/MobileRecipientInvitations.svelte');
  const props={inbox:{items:[invitation()]},status:'ready',error:null,busy:false,onLoad:()=>true};
  const rendered=render(Inbox,{props}).body;
  assert.match(rendered,/recipient-pending-invitations/);
  assert.match(rendered,/Ακαδημία Λευτεριά FC/);
  assert.match(rendered,/ΣΕ ΑΝΑΜΟΝΗ/);
  assert.match(rendered,/Δεν έχει αποσταλεί SMS/);
  assert.match(rendered,/Όρια οικογενειακής πρόσβασης/);
  assert.doesNotMatch(rendered,/secret-child-id|target_phone|parent_phone|recipient-accept-submit/);
  const empty=render(Inbox,{props:{...props,inbox:{items:[]}}}).body;
  assert.match(empty,/recipient-invites-empty/);
  const error=render(Inbox,{props:{...props,inbox:null,status:'error',error:'Η υπηρεσία δεν είναι διαθέσιμη'}}).body;
  assert.match(error,/recipient-invites-error/);
  assert.doesNotMatch(error,/ΣΕ ΑΝΑΜΟΝΗ/);
  const active=render(Inbox,{props:{...props,onLoadDelegation:()=>true,
    delegationStatus:'ready',delegationAvailability:{has_active_delegation:true}}}).body;
  assert.match(active,/recipient-delegation-active/);
  assert.match(active,/Μπορείς να δεις το πρόγραμμα και να δηλώσεις συμμετοχή/);
  assert.doesNotMatch(active,/secret-child-id|guardian_phone|target_phone|recipient-accept-submit/);
  const accept=render(Inbox,{props:{...props,onAccept:async()=>true}}).body;
  assert.match(accept,/recipient-accept-start/);
  assert.doesNotMatch(rendered,/recipient-accept-start/);
  assert.match(accept,/Δεν δημιουργείται λογαριασμός αθλητή/);
  const {default:Calendar}=await import('../src/lib/mobile/components/MobileDelegatedCalendar.svelte');
  const delegated=render(Calendar,{props:{calendar:calendarPayload(),status:'ready',error:null,
    onLoad:()=>true}}).body;
  assert.match(delegated,/family-delegated-calendar/);
  assert.match(delegated,/Προπόνηση Ακαδημίας/);
  assert.doesNotMatch(delegated,/calendar-rsvp-|availability|onRespond|Απαντήσεις συμμετοχής/);
  const writable=render(Calendar,{props:{calendar:calendarPayload(),status:'ready',error:null,
    onLoad:()=>true,onRespond:async()=>true}}).body;
  assert.match(writable,/delegated-rsvp-going/);
  assert.match(writable,/delegated-rsvp-not_going/);
  assert.doesNotMatch(delegated,/delegated-rsvp-controls/);
  const attached=render(Inbox,{props:{...props,onLoadCalendar:()=>true,
    delegationStatus:'ready',delegationAvailability:{has_active_delegation:true},
    delegatedCalendar:calendarPayload(),delegatedCalendarStatus:'ready'}}).body;
  assert.match(attached,/family-delegated-calendar/);
  const inactive=render(Inbox,{props:{...props,onLoadCalendar:()=>true,
    delegationStatus:'ready',delegationAvailability:{has_active_delegation:false}}}).body;
  assert.doesNotMatch(inactive,/family-delegated-calendar/);
  const route=readFileSync(new URL('../src/routes/app/[...path]/+page.svelte',import.meta.url),'utf8');
  assert.match(route,/activeTab === 'profile' && !composing/);
  assert.match(route,/\{#if role === 'fan'\}[\s\S]*?<MobileRecipientInvitations/);
  assert.match(route,/onLoad=\{\(\) => controller\.loadRecipientInvitations\(\)\}/);
  assert.match(route,/inbox=\{\$controller\.recipientInvitations\}/);
});
