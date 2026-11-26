import assert from 'node:assert/strict';
import test from 'node:test';
import {readFile} from 'node:fs/promises';
import {entryRouteGroups,generateEdge,reviewedAdminEntries,
  reviewedHiddenAdminEntries} from './generate-edge.mjs';

const contract=JSON.parse(await readFile(
  new URL('../../contracts/openapi.json',import.meta.url),'utf8'));
const repoFile=async path=>readFile(new URL('../../'+path,import.meta.url),'utf8');
const permitted=(method,path,doc=contract)=>entryRouteGroups(doc)
  .some(group=>group.method===method&&new RegExp(group.expression).test(path));

test('fixed WebAdmin contract inventory is reviewed exactly by path, method and security',()=>{
  const guarded=Object.entries(contract.paths).flatMap(([path,operations])=>
    Object.entries(operations).filter(([method,operation])=>path.startsWith('/api/admin/')&&
      JSON.stringify(operation.security)==='[{"WebAdmin":[]}]')
      .map(([method])=>[method,path]));
  assert.equal(guarded.length,121);
  const reviewed=new Set(reviewedAdminEntries.map(([method,path])=>method+'|'+path));
  assert.equal(reviewed.size,104);
  for(const [method,path] of reviewedAdminEntries){
    assert.equal(JSON.stringify(contract.paths[path]?.[method]?.security),'[{"WebAdmin":[]}]');
    assert.equal(permitted(method.toUpperCase(),
      path.replaceAll(/\{[A-Za-z_][A-Za-z0-9_]*\}/g,'11111111-2222-4333-8444-555555555555')),
      true,method+' '+path);
  }
  assert.equal(new Set(guarded.map(([method,path])=>method+'|'+path)).size,121);
  assert.match(generateEdge(contract,'srv1649602.hstgr.cloud','a'.repeat(64)),
    /handle @entry_get/);
});
test('club administration operations are reached but never wrong verbs, trailing paths or unknown APIs',()=>{
  for(const [method,path] of [
    ['GET','/api/admin/football/audiences'],
    ['GET','/api/admin/pos/sites/ci-allowed/stock-reconciliation'],
    ['GET','/api/admin/pos/cashier/registers/ci-restricted/sellable-stock'],
    ['POST','/api/admin/football/groups'],
    ['POST','/api/admin/football/groups/with-profile'],
    ['GET','/api/admin/website/sponsorships'],
    ['POST','/api/admin/website/sponsorships/draft'],
    ['POST','/api/admin/website/sponsorships/native'],
    ['POST','/api/admin/website/sponsorships/publication'],
    ['GET','/api/admin/football/players'],
    ['GET','/api/admin/football/group-players'],
    ['PUT','/api/admin/football/assignments'],
    ['GET','/api/admin/football/territories'],
    ['POST','/api/admin/football/territories'],
    ['GET','/api/admin/football/territories/access'],
    ['PUT','/api/admin/football/territories/access'],
    ['GET','/api/admin/football/territories/metrics'],
    ['GET','/api/admin/access/users'],
    ['POST','/api/admin/access/users'],
    ['POST','/api/admin/academy/office/registrations/enroll'],
    ['POST','/api/admin/access/users/status'],
    ['POST','/api/admin/access/users/role'],
    ['POST','/api/admin/staff/coaches'],
    ['GET','/api/admin/academy/drills'],
    ['POST','/api/admin/academy/drills'],
    ['PUT','/api/admin/academy/drills/11111111-2222-4333-8444-555555555555'],
    ['GET','/api/admin/academy/training/plans'],
    ['POST','/api/admin/academy/training/plans'],
    ['GET','/api/admin/academy/training/plans/11111111-2222-4333-8444-555555555555/drills'],
    ['PUT','/api/admin/academy/training/plans/11111111-2222-4333-8444-555555555555/drills'],
    ['POST','/api/admin/auth/profile/name'],
    ['POST','/api/admin/auth/profile/password'],
    ['GET','/api/admin/auth/profile/contact-email'],
    ['GET','/api/admin/hub/fan-subscriptions/overview'],
    ['GET','/api/admin/hub/classroom-curriculum'],
    ['PUT','/api/admin/hub/classroom-curriculum/11111111-1111-4111-8111-111111111111'],
    ['GET','/api/admin/hub/players/ci-player/learning-position'],
    ['PUT','/api/admin/hub/players/ci-player/learning-position'],
    ['POST','/api/admin/auth/profile/contact-email']
  ]){
    assert.equal(permitted(method,path),true,method+' '+path);
    assert.equal(permitted(method,path+'/extra'),false,method+' '+path+'/extra');
    assert.equal(permitted('OPTIONS',path),false,'unreviewed method '+path);
  }
  for(const method of ['GET','POST','PUT','PATCH','DELETE','OPTIONS']){
    for(const path of ['/api/admin/future','/api/admin/football/unknown',
      '/api/admin/football/territories/unknown','/api/admin/football/group-players/extra',
      '/api/admin/access/users/delete','/api/admin/website/sponsorships/unknown',
      '/api/health/live','/api/openapi/v1.json'])
      assert.equal(permitted(method,path),false,method+' '+path);
  }
  assert.equal(reviewedHiddenAdminEntries.length,25);
});
test('new shift-stock GET is reviewed by exact path, verb and current WebAdmin session',()=>{
  const path='/api/admin/pos/cashier/registers/{registerId}/sellable-stock';
  assert.equal(permitted('GET','/api/admin/pos/cashier/registers/ci-restricted/sellable-stock'),true);
  for(const method of ['POST','PUT','PATCH','DELETE','HEAD','OPTIONS']){
    assert.equal(permitted(method,'/api/admin/pos/cashier/registers/ci-restricted/sellable-stock'),false);
  }
  for(const suffix of ['/extra','/../../products','/other/stock']){
    assert.equal(permitted('GET','/api/admin/pos/cashier/registers/ci-restricted/sellable-stock'+suffix),false);
  }
  const tampered=structuredClone(contract);
  tampered.paths[path].get.security=[];
  assert.throws(()=>entryRouteGroups(tampered),/Reviewed application entry security changed/);
  const unauthorized=structuredClone(contract);
  delete unauthorized.paths[path].get;
  assert.throws(()=>entryRouteGroups(unauthorized),/Reviewed application entry security changed/);
});

test('stock reconciler proxy permits only the exact hidden authenticated read',()=>{
  const p='/api/admin/pos/sites/site-xyz/stock-reconciliation';
  assert.equal(permitted('GET',p),true);
  for(const verb of ['POST','PUT','PATCH','DELETE','OPTIONS'])
    assert.equal(permitted(verb,p),false);
  assert.equal(permitted('GET',p+'/other'),false);
  assert.equal(permitted('GET','/api/admin/pos/sites/site-xyz/inventory-write'),false);
});
test('new/ambiguous admin APIs require explicit security and route review before publication',()=>{
  const future=structuredClone(contract);
  future.paths['/api/admin/forgotten-unreviewed']={get:{security:[{WebAdmin:[]}]}};
  assert.throws(()=>generateEdge(future,'srv1649602.hstgr.cloud','a'.repeat(64)),
    /Unreviewed admin API/);
  const wrong=structuredClone(contract);
  wrong.paths['/api/admin/football/groups'].post.security=[];
  assert.throws(()=>generateEdge(wrong,'srv1649602.hstgr.cloud','a'.repeat(64)),
    /entry security|Unreviewed/);
});
test('hidden admin routes are pinned to independently authorized source instead of broad prefixes',async()=>{
  const files=[
    'backend/src/LefteriaFc.Api/Football/AdminFootballTerritoryEndpoints.cs',
    'backend/src/LefteriaFc.Api/Football/AdminFootballTerritoryAccessEndpoints.cs',
    'backend/src/LefteriaFc.Api/Audiences/AdminRosterPlayerEndpoints.cs',
    'backend/src/LefteriaFc.Api/WebAdmin/AdminUserDirectoryEndpoints.cs',
    'backend/src/LefteriaFc.Api/Marketing/AcademyIntakeAdminEndpoints.cs',
    'backend/src/LefteriaFc.Api/Crm/AcademyGuardianProofEndpoints.cs',
    'backend/src/LefteriaFc.Api/WebAdmin/AdminSelfServiceEndpoints.cs',
    'backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs'
  ];
  const sources=await Promise.all(files.map(repoFile));
  for(const source of sources)assert.match(source,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  for(const route of ['/api/admin/football/territories','/api/admin/football/group-players',
    '/api/admin/access/users','/api/admin/academy/intake',
    '/api/admin/academy/office/guardians',
    '/api/admin/auth/profile/name','/api/admin/auth/profile/password',
    '/api/admin/hub/fan-subscriptions/overview'])
    assert.ok(sources.some(source=>source.includes(route)),'No secured source path '+route);
  const generator=await repoFile('deploy/preview/generate-edge.mjs');
  assert.doesNotMatch(generator,/path_regexp entry_.*\^\/api\/admin\/\[\^\/\]/);
  assert.match(generator,/Unreviewed admin API requires explicit preview route approval/);
  assert.match(generator,/Reviewed application entry security changed/);
});

test('training drill builder permits only reviewed read and write verbs and requires WebAdmin security',()=>{
  const route='/api/admin/academy/training/plans/{planId}/drills';
  const path=route.replace('{planId}','11111111-2222-4333-8444-555555555555');
  for(const verb of ['POST','PATCH','DELETE','HEAD','OPTIONS'])
    assert.equal(permitted(verb,path),false,verb+' '+path);
  for(const method of ['get','put']){
    const tampered=structuredClone(contract);
    tampered.paths[route][method].security=[];
    assert.throws(()=>entryRouteGroups(tampered),/Reviewed application entry security changed/);
  }
});
