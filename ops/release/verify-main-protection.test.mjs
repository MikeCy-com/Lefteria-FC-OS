import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {verifyMainProtection,REQUIRED_MAIN_CHECKS}
  from './verify-main-protection.mjs';

const sha='a'.repeat(40);
const good=()=>({
  name:'main',commit:{sha},protected:true,
  protection:{
    enabled:true,
    required_status_checks:{
      enforcement_level:'everyone',
      checks:REQUIRED_MAIN_CHECKS.map(context=>({context,app_id:null})),
      contexts:[...REQUIRED_MAIN_CHECKS]
    }
  }
});
const mock=(branch=good(),status=200)=>async(url,opts)=>{
  assert.equal(url,'https://api.github.com/repos/MikeCy-com/Lefteria-FC-OS/branches/main');
  assert.equal(opts.method,'GET');
  assert.equal(opts.redirect,'error');
  return {ok:status===200,status,json:async()=>structuredClone(branch)};
};

test('requires all six exact CI contexts on protected main, not merely green jobs',async()=>{
  const report=await verifyMainProtection(sha,{fetchImpl:mock()});
  assert.equal(report.verified,true);
  assert.deepEqual(report.blocked,[]);
  assert.deepEqual(report.missing_required_checks,[]);
  assert.match(report.caveat,/separate operator verification/);
});
test('actual unprotected main is blocked even when every check exists',async()=>{
  const branch=good();
  branch.protected=false;
  branch.protection.enabled=false;
  const report=await verifyMainProtection(sha,{fetchImpl:mock(branch)});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.includes('main_branch_not_protected'));
});
test('missing mandatory browser or API checks fail closed',async()=>{
  const branch=good();
  branch.protection.required_status_checks.checks=[];
  branch.protection.required_status_checks.contexts=
    REQUIRED_MAIN_CHECKS.filter(context=>context!=='Browser screenshots and viewport overflow');
  const report=await verifyMainProtection(sha,{fetchImpl:mock(branch)});
  assert.equal(report.verified,false);
  assert.deepEqual(report.missing_required_checks,
    ['Browser screenshots and viewport overflow']);
});
test('non-admin-only enforcement, stale main commit and malformed branch are rejected',async()=>{
  const branch=good();
  branch.protection.required_status_checks.enforcement_level='non_admins';
  branch.commit.sha='b'.repeat(40);
  const report=await verifyMainProtection(sha,{fetchImpl:mock(branch)});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.includes('not_exact_current_main'));
  assert.ok(report.blocked.includes('status_checks_not_enforced_for_everyone'));
  await assert.rejects(()=>verifyMainProtection(sha,{fetchImpl:mock(null)}),
    /Invalid GitHub branch evidence/);
});
test('rejects unsafe tokens, malformed SHAs and unavailable GitHub evidence',async()=>{
  let calls=0;
  const never=async()=>{calls++;throw new Error('unexpected fetch');};
  for(const bad of ['','deadbeef','Z'.repeat(40)]){
    await assert.rejects(()=>verifyMainProtection(bad,{fetchImpl:never}),/SHA/);
  }
  await assert.rejects(()=>verifyMainProtection(sha,{fetchImpl:never,token:'a\r\nb'}),/token/);
  assert.equal(calls,0);
  await assert.rejects(()=>verifyMainProtection(sha,{fetchImpl:mock(good(),403)}),/unavailable/);
});
test('manual release preflight invokes governance guard without any deployment permission',()=>{
  const yaml=readFileSync(
    new URL('../../.github/workflows/production-release-preflight.yml',import.meta.url),
    'utf8');
  assert.match(yaml,/verify-main-protection[.]mjs/);
  assert.match(yaml,/contents: read/);
  assert.match(yaml,/actions: read/);
  assert.doesNotMatch(yaml,/^[ \t]*environment:[ \t]*(?:production|live)[ \t]*$/m);
});
