import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { bearerRouteGroups, entryRouteGroups, generateEdge, protectedOperations, publicWebExpression, publicApiExpression, reviewedPublicReads } from './generate-edge.mjs';

const contract = JSON.parse(await readFile(new URL('../../contracts/openapi.json', import.meta.url), 'utf8'));
const hostname = 'srv1649602.hstgr.cloud';
const digest = 'a'.repeat(64);
const permits = (document, method, path) => bearerRouteGroups(document).some(group =>
  group.method === method && new RegExp(group.expression).test(path));

test('only the seventy explicitly declared Mobile operations receive Bearer routing', () => {
  const operations = protectedOperations(contract);
  assert.equal(operations.length, 70);
  for (const operation of operations) {
    const path = operation.path.replace(/\{invoiceId\}/g,
      '11111111-2222-4333-8444-555555555555').replace(/\{[^}]+\}/g, 'record');
    assert.ok(permits(contract, operation.method, path), `${operation.method} ${path}`);
    assert.equal(permits(contract, 'OPTIONS', path), false);
    assert.equal(permits(contract, operation.method, path + '/extra'), false);
  }
  // Private PDF routes must stay read-only, exact and ID-constrained.
  const invoiceId='11111111-2222-4333-8444-555555555555';
  assert.equal(permits(contract,'GET','/api/mobile/finance/invoices/multi.pdf'),true);
  assert.equal(permits(contract,'POST','/api/mobile/finance/invoices/multi.pdf'),false);
  assert.equal(permits(contract,'GET','/api/mobile/finance/invoices/multi.pdf/extra'),false);
  assert.equal(permits(contract,'GET','/api/mobile/finance/invoices/'+invoiceId+'.pdf'),true);
  assert.equal(permits(contract,'POST','/api/mobile/finance/invoices/'+invoiceId+'.pdf'),false);
  assert.equal(permits(contract,'GET','/api/mobile/finance/invoices/'+invoiceId+'.pdf/extra'),false);
  assert.equal(permits(contract,'GET','/api/mobile/finance/invoices/not-a-uuid.pdf'),false);
  assert.equal(permits(contract,'GET','/api/mobile/finance/invoices/'+invoiceId+'.exe'),false);
  for (const path of [
    '/api/mobile/auth/request-otp', '/api/mobile/auth/verify-otp', '/api/mobile/auth/register',
    '/api/mobile/auth/exchange-legacy', '/api/health/live', '/api/uploads/avatars/photo.jpg',
    '/api/mobile/future-public-route', '/app', '/api/mobile/calendar/event/id/responses/extra',
    '/api/mobile/calendar/event/nested/id/responses', '/api/mobile/calendar//id/responses'
  ]) {
    for (const method of ['GET', 'POST', 'PUT']) assert.equal(permits(contract, method, path), false, `${method} ${path}`);
  }
  assert.equal(permits(contract, 'POST', '/api/mobile/family/children/player-id/invitations'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/invitations/00000000-0000-0000-0000-000000000001/accept'), true);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/invitations/00000000-0000-0000-0000-000000000001/accept'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/invitations/00000000-0000-0000-0000-000000000001/accept/extra'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/children/player-id/revoke'), true);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/children/player-id/revoke'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/children/player-id/revoke/extra'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/invitations/00000000-0000-4000-8000-000000000001/cancel'), true);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/invitations/00000000-0000-4000-8000-000000000001/cancel'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/children/player-id/invitations/extra'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/invitations/00000000-0000-4000-8000-000000000001/decline'), true);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/invitations/00000000-0000-4000-8000-000000000001/decline'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/invitations/00000000-0000-4000-8000-000000000001/decline/extra'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/active-children'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/active-children'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/active-children/extra'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/active-delegation'), true);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/calendar'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/availability'), true);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/availability'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/availability/another'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/calendar'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/calendar/private'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/active-delegation'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/active-delegation/extra'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/invitations'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/invitations'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/invitations/extra'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/outgoing-invitations'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/family/outgoing-invitations'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/outgoing-invitations/extra'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/family'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/family/unknown'), false);
  const guardianRoutes = [
    ['GET', '/api/mobile/family/guardian-permissions'],
    ['POST', '/api/mobile/family/children/player-id/guardian-permissions'],
    ['POST', '/api/mobile/family/guardian-permissions/bulk'],
    ['POST', '/api/mobile/family/guardian-permissions/00000000-0000-4000-8000-000000000001/revoke']
  ];
  for (const [method, path] of guardianRoutes) {
    assert.equal(permits(contract, method, path), true);
    assert.equal(permits(contract, method === 'GET' ? 'POST' : 'GET', path), false);
  }
  const coachTargets='/api/mobile/coach/feedback/targets';
  assert.equal(permits(contract,'GET',coachTargets),true);
  assert.equal(permits(contract,'GET',coachTargets+'/extra'),false);
  for(const method of ['POST','PUT','DELETE','PATCH','OPTIONS'])
    assert.equal(permits(contract,method,coachTargets),false);
  const coachSend='/api/mobile/coach/feedback/drafts/11111111-2222-4333-8444-555555555555/send';
  assert.equal(permits(contract,'POST',coachSend),true);
  for(const method of ['GET','PUT','DELETE','PATCH','OPTIONS'])
    assert.equal(permits(contract,method,coachSend),false);
  assert.equal(permits(contract,'POST',coachSend+'/extra'),false);
  assert.equal(permits(contract,'GET','/api/mobile/player/coach-feedback'),true);
  assert.equal(permits(contract,'GET','/api/mobile/player/coach-feedback/extra'),false);
  for(const method of ['POST','PUT','DELETE','PATCH','OPTIONS'])
    assert.equal(permits(contract,method,'/api/mobile/player/coach-feedback'),false);
  const coachApproval='/api/mobile/coach/feedback/drafts/11111111-2222-4333-8444-555555555555/approve';
  assert.equal(permits(contract,'POST',coachApproval),true);
  for(const method of ['GET','PUT','DELETE','PATCH','OPTIONS'])
    assert.equal(permits(contract,method,coachApproval),false);
  assert.equal(permits(contract,'POST',coachApproval+'/extra'),false);
  const coachDraft='/api/mobile/coach/feedback/drafts';
  assert.equal(permits(contract,'POST',coachDraft),true);
  for(const method of ['GET','PUT','DELETE','PATCH','OPTIONS'])
    assert.equal(permits(contract,method,coachDraft),false);
  assert.equal(permits(contract,'POST',coachDraft+'/extra'),false);
  assert.equal(permits(contract, 'GET', '/api/mobile/chat/groups'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/chat/groups'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/chat/groups/lefteria_fc/first_team/messages'), true);
  assert.equal(permits(contract, 'POST', '/api/mobile/chat/groups/lefteria_fc/first_team/messages'), true);
  const ownMessage='/api/mobile/chat/groups/lefteria_fc/first_team/messages/11111111-2222-4333-8444-555555555555';
  assert.equal(permits(contract,'PUT',ownMessage),true);
  assert.equal(permits(contract,'DELETE',ownMessage),true);
  assert.equal(permits(contract,'POST',ownMessage),false);
  assert.equal(permits(contract,'GET',ownMessage),false);
  assert.equal(permits(contract,'PATCH',ownMessage),false);
  assert.equal(permits(contract,'PUT',ownMessage+'/extra'),false);
  assert.equal(permits(contract,'DELETE',ownMessage+'/extra'),false);
  assert.equal(permits(contract,'POST',ownMessage+'/reactions'),true);
  assert.equal(permits(contract,'GET',ownMessage+'/reactions'),false);
  assert.equal(permits(contract,'PUT',ownMessage+'/reactions'),false);
  assert.equal(permits(contract,'DELETE',ownMessage+'/reactions'),false);
  assert.equal(permits(contract,'POST',ownMessage+'/reactions/extra'),false);
  assert.equal(permits(contract,'POST',ownMessage+'/seen'),true);
  assert.equal(permits(contract,'GET',ownMessage+'/readers'),true);
  assert.equal(permits(contract,'GET',ownMessage+'/seen'),false);
  assert.equal(permits(contract,'POST',ownMessage+'/readers'),false);
  assert.equal(permits(contract,'PATCH',ownMessage+'/seen'),false);
  assert.equal(permits(contract,'GET',ownMessage+'/readers/extra'),false);
  assert.equal(permits(contract,'POST','/api/mobile/chat/groups/lefteria_fc/first_team/messages/reactions'),false);
  const privateId='11111111-2222-4333-8444-555555555555';
  const privateMessage='aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
  assert.equal(permits(contract,'GET','/api/mobile/private/contacts'),true);
  assert.equal(permits(contract,'GET','/api/mobile/private/conversations'),true);
  assert.equal(permits(contract,'POST','/api/mobile/private/invitations'),true);
  assert.equal(permits(contract,'POST','/api/mobile/private/conversations/'+privateId+'/respond'),true);
  assert.equal(permits(contract,'GET','/api/mobile/private/conversations/'+privateId+'/messages'),true);
  assert.equal(permits(contract,'POST','/api/mobile/private/conversations/'+privateId+'/messages'),true);
  assert.equal(permits(contract,'PUT','/api/mobile/private/conversations/'+privateId+'/messages/'+privateMessage),true);
  assert.equal(permits(contract,'DELETE','/api/mobile/private/conversations/'+privateId+'/messages/'+privateMessage),true);
  assert.equal(permits(contract,'DELETE','/api/mobile/private/conversations/'+privateId+'/for-me'),true);
  for(const [method,path] of [
    ['GET','/api/mobile/private/invitations'],
    ['POST','/api/mobile/private/contacts'],
    ['DELETE','/api/mobile/private/conversations'],
    ['GET','/api/mobile/private/conversations/'+privateId+'/respond'],
    ['GET','/api/mobile/private/conversations/'+privateId+'/for-me'],
    ['POST','/api/mobile/private/conversations/'+privateId+'/for-me'],
    ['GET','/api/mobile/private/conversations/'+privateId+'/messages/'+privateMessage],
    ['PATCH','/api/mobile/private/conversations/'+privateId+'/messages/'+privateMessage],
    ['DELETE','/api/mobile/private/conversations/'+privateId+'/messages/'+privateMessage+'/extra'],
    ['GET','/api/mobile/private/contact-phones']
  ])assert.equal(permits(contract,method,path),false,method+' '+path);
  // Publishing and removal remain web-admin-only, while mobile members can
  // set or remove an allowed reaction only on an exact scoped notice ID.
  const appReaction='/api/mobile/app/announcements/lefteria_fc_academy/u13/ann_'+
    'a'.repeat(32)+'/reactions';
  const appSeen='/api/mobile/app/announcements/lefteria_fc_academy/u13/ann_'+
    'a'.repeat(32)+'/seen';
  assert.equal(permits(contract,'POST',appSeen),true);
  for(const method of ['GET','PUT','DELETE','PATCH'])
    assert.equal(permits(contract,method,appSeen),false);
  assert.equal(permits(contract,'POST',appSeen+'/extra'),false);
  const mediaPath='/api/mobile/app/announcements/lefteria_fc_academy/u13/ann_'+
    'a'.repeat(32)+'/media/11111111-2222-4333-8444-555555555555';
  assert.equal(permits(contract,'GET',mediaPath),true);
  for(const method of ['POST','PUT','DELETE','PATCH'])
    assert.equal(permits(contract,method,mediaPath),false);
  assert.equal(permits(contract,'GET',mediaPath+'/extra'),false);
  assert.equal(permits(contract,'POST',appReaction),true);
  for(const method of ['GET','PUT','DELETE','PATCH'])
    assert.equal(permits(contract,method,appReaction),false);
  assert.equal(permits(contract,'POST',appReaction+'/extra'),false);
  assert.equal(permits(contract,'POST','/api/mobile/app/announcements/lefteria_fc_academy/u13'),false);
  // Announcements are app-only, GET and scoped to a canonical room.
  const pushConfig='/api/mobile/app/push';
  const pushSubscriptions='/api/mobile/app/push/subscriptions';
  assert.equal(permits(contract,'GET',pushConfig),true);
  assert.equal(permits(contract,'POST',pushConfig),false);
  assert.equal(permits(contract,'GET',pushSubscriptions),false);
  assert.equal(permits(contract,'POST',pushSubscriptions),true);
  assert.equal(permits(contract,'DELETE',pushSubscriptions),true);
  assert.equal(permits(contract,'PATCH',pushSubscriptions),false);
  assert.equal(permits(contract,'POST',pushSubscriptions+'/extra'),false);
  const notificationInbox='/api/mobile/app/notifications';
  assert.equal(permits(contract,'GET',notificationInbox),true);
  assert.equal(permits(contract,'GET',notificationInbox+'/unexpected'),false);
  assert.equal(permits(contract,'POST',notificationInbox),false);
  assert.equal(permits(contract,'DELETE',notificationInbox),false);
  const appNotice='/api/mobile/app/announcements/lefteria_fc_academy/u13';
  assert.equal(permits(contract,'GET',appNotice),true);
  for(const [method,path] of [
    ['POST',appNotice],['PUT',appNotice],['DELETE',appNotice],
    ['GET',appNotice+'/extra'],['GET','/api/mobile/app/announcements'],
    ['GET','/api/mobile/app/announcements/lefteria_fc_academy'],
    ['GET','/api/mobile/app/announcements/lefteria_fc_academy/u13/seen']
  ])assert.equal(permits(contract,method,path),false,method+' '+path);
  assert.equal(permits(contract, 'GET', '/api/mobile/chat/groups/lefteria_fc_academy/under_13/messages'), true);
  assert.equal(permits(contract, 'DELETE', '/api/mobile/chat/groups/lefteria_fc/first_team/messages'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/chat/groups/lefteria_fc/first_team/messages/extra'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/chat/groups/lefteria_fc/first_team/messages/extra'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/chat/groups/extra'), false);
  assert.equal(permits(contract, 'POST', '/api/mobile/calendar'), false);
  assert.equal(permits(contract, 'GET', '/api/mobile/profile'), false);
});

test('anonymous and newly declared non-Mobile operations do not enter the protected Mobile inventory', () => {
  const changed = structuredClone(contract);
  changed.paths['/api/mobile/anonymous'] = { get: { security: [] } };
  changed.paths['/api/mobile/unmarked'] = { post: {} };
  changed.paths['/api/mobile/auth/exchange-legacy'].post.security = [{ LegacyMobile: [] }];
  assert.equal(protectedOperations(changed).length, 70);
  assert.equal(permits(changed, 'GET', '/api/mobile/anonymous'), false);
  assert.equal(permits(changed, 'POST', '/api/mobile/unmarked'), false);
});

test('ambiguous security, global defaults and unsafe templates fail closed', () => {
  for (const security of [[{ Mobile: [] }, {}], [{ Mobile: [], Other: [] }], [{ Mobile: ['scope'] }]]) {
    const changed = structuredClone(contract);
    changed.paths['/api/mobile/calendar'].get.security = security;
    assert.throws(() => protectedOperations(changed), /Ambiguous/);
  }
  assert.throws(() => protectedOperations({ ...contract, security: [{ Mobile: [] }] }), /Explicit/);
  for (const path of ['/outside', '/api/mobile/a*', '/api/mobile/a.b', '/api/mobile/a//b', '/api/mobile/{unknown}', '/api/mobile/{part}suffix']) {
    const changed = structuredClone(contract);
    changed.paths[path] = { get: { security: [{ Mobile: [] }] } };
    assert.throws(() => protectedOperations(changed));
  }
});

test('generated edge removes the browser Basic gate and preserves application authentication', () => {
  const output = generateEdge(contract, hostname, digest);
  assert.match(output, /https:\/\/srv1649602\.hstgr\.cloud:8443/);
  assert.match(output, /auto_https off/);
  assert.match(output, /tls \/certificates\/current\/fullchain\.pem \/certificates\/current\/privkey\.pem/);
  assert.doesNotMatch(output, /basic_auth|PREVIEW_GATE_HASH|WWW-Authenticate|header_regexp bearer_/);
  assert.match(output, /request_header @cached_basic -Authorization/);
  assert.equal((output.match(/header_up -Authorization/g) ?? []).length, 3);
  assert.equal((output.match(/header_up -Cookie/g) ?? []).length, 2);
  assert.ok(output.indexOf('handle @private_') < output.indexOf('handle @api {'));
  assert.match(output, /handle @api \{[\s\S]*?respond "" 404/);
  assert.doesNotMatch(output, /listen (?:80|443)\b|:80\s*\{|:443\s*\{|tls internal|on_demand/);
  assert.match(output, /X-Robots-Tag "noindex, nofollow, noarchive"/);
  assert.match(output, /Cache-Control "no-store"/);
  assert.throws(() => generateEdge(contract, 'lefteriafc.cy', digest), /hostname/);
  assert.throws(() => generateEdge(contract, hostname, 'invalid'), /SHA-256/);
});

test('public reads remain credential-free and private restored media remains unavailable', () => {
  assert.equal(reviewedPublicReads(contract).length, 4);
  const web = new RegExp(publicWebExpression), api = new RegExp(publicApiExpression);
  for (const path of ['/', '/__data.json', '/team', '/academy/__data.json', '/academy/philosophy', '/academy/philosophy/__data.json', '/privacy', '/privacy/__data.json', '/terms', '/terms/__data.json', '/news', '/news/story--id', '/news/story--id/__data.json', '/_app/immutable/chunks/part.js', '/club/logo512.png', '/mobile/club-logo.png', '/favicon.ico']) assert.ok(web.test(path), path);
  for (const path of ['/admin', '/admin/login', '/admin/__data.json', '/app', '/app/login', '/api/admin/auth/me', '/_app/../admin', '/api/mobile/auth/me', '/news/id/extra', '/privacy/extra', '/terms/extra', '/academy/philosophy/extra']) assert.equal(web.test(path), false, path);
  for (const path of ['/api/public/site', '/api/public/news', '/api/public/news/id', '/api/public/sections']) assert.ok(api.test(path), path);
  for (const path of ['/api/public/new-route', '/api/public/news/id/extra', '/api/admin/auth/login', '/api/mobile/profile', '/api/health/ready']) assert.equal(api.test(path), false, path);
  const changed = structuredClone(contract);
  changed.paths['/api/public/site'].get.security = [{ WebAdmin: [] }];
  assert.throws(() => generateEdge(changed, hostname, digest), /anonymous/);
  const output = generateEdge(contract, hostname, digest);
  assert.ok(output.indexOf('handle @public_web') < output.indexOf('handle @api {'));
  assert.match(output, /@public_api \{\n    method GET\n/);
  assert.match(output, /@legacy_upload[\s\S]*handle @legacy_upload[\s\S]*respond "" 404/);
});

test('only reviewed login/session, club editor and sanitized-avatar operations reach their application policies', () => {
  const adminMedia='/api/admin/communications/announcement-drafts/ann_'+
    'a'.repeat(32)+'/media';
  const adminMediaDelete=adminMedia+'/11111111-2222-4333-8444-555555555555';
  assert.equal(entryRouteGroups(contract).some(group=>group.method==='POST'&&
    new RegExp(group.expression).test(adminMedia)),true);
  assert.equal(entryRouteGroups(contract).some(group=>group.method==='DELETE'&&
    new RegExp(group.expression).test(adminMediaDelete)),true);
  assert.equal(permits(contract,'POST',adminMedia),false);
  const adminReaders='/api/admin/communications/app-announcements/ann_'+
    'a'.repeat(32)+'/readers';
  assert.equal(entryRouteGroups(contract).some(group=>group.method==='GET'&&
    new RegExp(group.expression).test(adminReaders)),true);
  assert.equal(permits(contract,'GET',adminReaders),false);
  const groups = entryRouteGroups(contract);
  const entryPermits = (method,path) => groups.some(group => group.method === method && new RegExp(group.expression).test(path));
  for (const [method,path] of [['POST','/api/admin/auth/login'],['POST','/api/admin/auth/logout'],['GET','/api/admin/auth/me'],
    ['GET','/api/admin/club'],['PUT','/api/admin/club'],
    ['GET','/api/admin/communications/announcement-drafts'],
    ['POST','/api/admin/communications/announcement-drafts'],
    ['POST','/api/admin/communications/announcement-drafts/ann_12345678901234567890123456789012/publish'],
    ['GET','/api/admin/communications/app-announcements'],
    ['DELETE','/api/admin/communications/app-announcements/ann_12345678901234567890123456789012'],
    ['POST','/api/mobile/auth/request-otp'],['POST','/api/mobile/auth/verify-otp'],['POST','/api/mobile/auth/register-fan'],
    ['POST','/api/mobile/auth/exchange-legacy'],['GET','/api/uploads/avatars/opaque.jpg'],['HEAD','/api/uploads/avatars/opaque.jpg']]) {
    assert.equal(entryPermits(method,path),true,method+' '+path);
    assert.equal(entryPermits(method,path+'/extra'),false);
  }
  for (const path of ['/api/health/ready','/api/openapi/v1.json','/api/mobile/auth/future','/api/admin/future','/api/uploads/private.jpg','/api/uploads/avatars/'])
    assert.equal(entryPermits('GET',path),false,path);
  for (const method of ['POST','DELETE','PATCH','HEAD','OPTIONS']) assert.equal(entryPermits(method,'/api/admin/club'),false);
  const draft='/api/admin/communications/announcement-drafts';
  const published='/api/admin/communications/app-announcements';
  const noticeId='ann_12345678901234567890123456789012';
  for(const [method,path] of [
    ['DELETE',draft],['PUT',draft],['PATCH',draft],
    ['GET',draft+'/'+noticeId+'/publish'],
    ['DELETE',draft+'/'+noticeId+'/publish'],
    ['GET',published+'/'+noticeId],['POST',published],
    ['POST',published+'/'+noticeId],['PUT',published+'/'+noticeId],
    ['GET',draft+'/'+noticeId+'/publish/extra'],
    ['DELETE',published+'/'+noticeId+'/extra'],
    ['GET','/api/admin/communications/announcement-drafts/'+noticeId],
    ['GET','/api/admin/communications/user-directory']
  ])assert.equal(entryPermits(method,path),false,method+' '+path);
  for (const [path,method,security] of [['/api/admin/auth/me','get',[]],['/api/admin/auth/login','post',[{Mobile:[]}]],['/api/admin/club','get',[]],['/api/admin/club','put',[]],
    [draft,'get',[]],[draft,'post',[]],[draft+'/{id}/publish','post',[]],
    [published,'get',[]],[published+'/{id}','delete',[]]]) {
    const changed=structuredClone(contract); changed.paths[path][method].security=security;
    assert.throws(()=>generateEdge(changed,hostname,digest),/entry security|Unexpected Mobile operation path/);
  }
  const output=generateEdge(contract,hostname,digest);
  assert.ok(output.indexOf('handle @entry_get')<output.indexOf('handle @legacy_upload'));
});

test('error routes apply the same security headers without replacing status or authentication challenges', () => {
  const output = generateEdge(contract, hostname, digest);
  const errorRoute = output.split('  handle_errors {\n')[1]?.split('\n  }')[0];
  assert.ok(errorRoute, 'Authentication errors need their own header handler.');
  for (const line of [
    'X-Frame-Options DENY', 'X-Content-Type-Options nosniff', 'Referrer-Policy no-referrer',
    'X-Robots-Tag "noindex, nofollow, noarchive"', 'Cache-Control "no-store"', '-Server'
  ]) {
    assert.ok(errorRoute.includes(line));
    assert.equal(output.split(line).length - 1, 2, 'Normal and error response headers must agree.');
  }
  assert.ok(errorRoute.includes('respond "" {err.status_code}'));
  assert.doesNotMatch(errorRoute, /WWW-Authenticate|err\.(?:message|trace|id)|reverse_proxy|respond[^\n]*200/i);
});
