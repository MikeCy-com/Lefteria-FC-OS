import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {requireIsolatedStagingOrigin,checkStagingMvp,STAGING_CHECKS}
  from './staging-mvp-preflight.mjs';

const validOrigin='https://srv1649602.hstgr.cloud:8443';
const mock=(changes={})=>async (url,options)=>{
  assert.equal(new URL(url).origin,validOrigin);
  assert.equal(options.method,'GET');
  assert.equal(options.redirect,'manual');
  assert.equal(options.credentials,'omit');
  const path=new URL(url).pathname;
  const chosen=changes[path];
  if(chosen)return chosen;
  if(path==='/api/health/live'||path==='/api/health/ready')
    return Response.json({status:path.endsWith('live')?'live':'ready'});
  if(path==='/'||path==='/admin/login')
    return new Response('<html>Preview</html>',{status:200,
      headers:{'content-type':'text/html; charset=utf-8'}});
  if(['/api/admin/pos/sites','/api/admin/finance/payabl/payment-requests',
    '/api/mobile/hub/fan-subscription/plans'].includes(path))
    return Response.json({code:'mvp_feature_disabled',
      detail:'Optional pilot feature not approved'}, {status:503});
  return new Response('{}',{status:401,headers:{'content-type':'application/json'}});
};

test('public production hostnames, HTTP and hostile redirectable origins are rejected',()=>{
  for(const address of [
    'https://lefteriafc.cy','https://www.lefteriafc.cy',
    'http://srv1649602.hstgr.cloud:8443',
    'https://otherdomain.test','https://evil.hstgr.cloud/secret',
    'https://user:password@srv1649602.hstgr.cloud:8443',
    'https://srv1649602.hstgr.cloud:8443?admin=1',
    'https://staging.lefteriafc.cy/hello','https://staging.lefteriafc.cy#frag'
  ])assert.throws(()=>requireIsolatedStagingOrigin(address));
  assert.equal(requireIsolatedStagingOrigin(validOrigin),validOrigin);
  assert.equal(requireIsolatedStagingOrigin('https://staging.lefteriafc.cy'),
    'https://staging.lefteriafc.cy');
});

test('staging preflight requires real live/ready, login and anonymous-denial evidence',async()=>{
  const report=await checkStagingMvp({origin:validOrigin,fetcher:mock()});
  assert.equal(report.ready,true);
  assert.equal(report.checks,STAGING_CHECKS.length);
  assert.equal(report.passed,STAGING_CHECKS.length);
  assert.ok(report.warnings.some(x=>x.includes('SHA')));
});
test('database unavailable, leaked private data and preview redirect all fail closed',async()=>{
  const db=await checkStagingMvp({origin:validOrigin,fetcher:mock({
    '/api/health/ready':Response.json({status:'unavailable'},{status:503})
  })});
  assert.equal(db.ready,false);
  const leak=await checkStagingMvp({origin:validOrigin,fetcher:mock({
    '/api/mobile/family':Response.json({children:[]})
  })});
  assert.equal(leak.ready,false);
  const redir=await checkStagingMvp({origin:validOrigin,fetcher:mock({
    '/admin/login':new Response(null,{status:302,headers:{location:'https://lefteriafc.cy'}})
  })});
  assert.equal(redir.ready,false);
});
test('credentials stay on pre-approved origin, are never included in results or logged',async()=>{
  let seen=0;
  const secret='Basic test-private-value';
  const report=await checkStagingMvp({origin:validOrigin,
    gateAuthorization:secret,
    fetcher:async(url,opts)=>{
      seen++;
      assert.equal(new URL(url).origin,validOrigin);
      assert.equal(opts.headers.Authorization,secret);
      return mock()(url,opts);
    }});
  assert.equal(seen,STAGING_CHECKS.length);
  assert.equal(report.ready,true);
  assert.ok(!JSON.stringify(report).includes(secret));
  await assert.rejects(()=>checkStagingMvp({origin:validOrigin,
    gateAuthorization:'Basic good\r\nInjected: value',fetcher:mock()}));
});
test('failed preflight fetch is a blocked result rather than an exception or fabricated pass',async()=>{
  const report=await checkStagingMvp({origin:validOrigin,
    fetcher:async(url,opts)=>{
      if(new URL(url).pathname==='/api/mobile/players-list')
        throw new Error('private credential must not leak');
      return mock()(url,opts);
    }});
  assert.equal(report.ready,false);
  assert.ok(report.evidence.some(x=>x.reason==='request_failed'));
  assert.ok(!JSON.stringify(report).includes('private credential'));
});

test('staging permission readiness queries have no database writes and pin core CRUD grants',()=>{
  const sql=readFileSync(new URL('./academy-mvp-permission-preflight.sql',import.meta.url),'utf8');
  const code=sql.split('\n').filter(line=>!line.trimStart().startsWith('--')).join('\n');
  assert.match(code,/BEGIN TRANSACTION READ ONLY/);
  assert.match(code,/ROLLBACK;/);
  assert.match(code,/has_function_privilege\(v_admin,v_oid,'EXECUTE'\)/);
  assert.match(code,/has_function_privilege\(v_mobile,v_oid,'EXECUTE'\)/);
  assert.match(code,/read_admin_academy_training_attendance/);
  assert.match(code,/create_admin_academy_location_group_with_profile/);
  assert.match(code,/record_admin_academy_training_attendance/);
  assert.match(code,/issue_guardian_permission/);
  assert.doesNotMatch(code,/\b(?:GRANT|REVOKE|TRUNCATE|INSERT INTO|UPDATE |DELETE FROM|ALTER TABLE|DROP TABLE)\b/i);
});

test('Academy MVP preflight requires a real disabled-feature code, never a generic 503 or 401',async()=>{
  for(const url of ['/api/admin/pos/sites',
    '/api/admin/finance/payabl/payment-requests',
    '/api/mobile/hub/fan-subscription/plans']){
    for(const response of [
      Response.json({code:'service_unavailable'}, {status:503}),
      Response.json({code:'not_authenticated'}, {status:401}),
      Response.json({plans:[]}, {status:200})
    ]){
      const report=await checkStagingMvp({origin:validOrigin,fetcher:mock({[url]:response})});
      assert.equal(report.ready,false,'Optional feature was not proven disabled: '+url);
      assert.ok(report.evidence.some(row=>row.path.startsWith(url) && !row.ok));
    }
  }
});

test('a successful HTML status without a document is not staging page acceptance',async()=>{
  for(const bad of [
    new Response('',{status:200,headers:{'content-type':'text/html'}}),
    new Response('Maintenance only',{status:200,headers:{'content-type':'text/html'}}),
    new Response('{"status":"ready"}',{status:200,headers:{'content-type':'text/html'}})
  ]){
    const report=await checkStagingMvp({origin:validOrigin,
      fetcher:mock({'/admin/login':bad})});
    assert.equal(report.ready,false);
    assert.ok(report.evidence.some(item=>
      item.path==='/admin/login'&&!item.ok));
  }
});

test('private Academy enrollment and roster APIs must deny anonymous staging access',async()=>{
  for(const protectedRoute of [
    '/api/admin/academy/intake',
    '/api/admin/football/players'
  ]){
    const report=await checkStagingMvp({origin:validOrigin,fetcher:mock({
      [protectedRoute]:Response.json({children:[],players:[]},{status:200})
    })});
    assert.equal(report.ready,false);
    assert.ok(report.evidence.some(item=>
      item.path.startsWith(protectedRoute)&&!item.ok));
  }
});
