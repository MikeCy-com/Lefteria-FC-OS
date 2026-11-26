import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {verifyExactMainCi,RELEASE_REPOSITORY,REQUIRED_WORKFLOWS}
  from './verify-exact-ci.mjs';

const sha='a'.repeat(40);
const otherSha='b'.repeat(40);
const expectedNames=Object.keys(REQUIRED_WORKFLOWS);
const successfulRun=(name,id,overrides={})=>({
  id,name,event:'push',head_branch:'main',head_sha:sha,
  status:'completed',conclusion:'success',
  repository:{full_name:RELEASE_REPOSITORY},...overrides
});
const fixtures=()=>({
  mainSha:sha,
  runs:[successfulRun(expectedNames[0],201),successfulRun(expectedNames[1],202)],
  jobs:{
    201:REQUIRED_WORKFLOWS[expectedNames[0]].map((name,id)=>({
      id:1000+id,name,status:'completed',conclusion:'success',head_sha:sha
    })),
    202:REQUIRED_WORKFLOWS[expectedNames[1]].map((name,id)=>({
      id:2000+id,name,status:'completed',conclusion:'success',head_sha:sha
    }))
  }
});
function mock(data){
  const requests=[];
  const fetchImpl=async(url,opts)=>{
    const parsed=new URL(url);
    requests.push({url:parsed.toString(),opts});
    assert.equal(parsed.origin,'https://api.github.com');
    assert.equal(parsed.pathname.startsWith('/repos/'+RELEASE_REPOSITORY+'/'),true);
    assert.equal(opts.method,'GET');
    assert.equal(opts.redirect,'error');
    let value;
    if(parsed.pathname.endsWith('/branches/main'))
      value={commit:{sha:data.mainSha}};
    else if(parsed.pathname.endsWith('/actions/runs'))
      value={total_count:data.runCount??data.runs.length,workflow_runs:data.runs};
    else{
      const matched=/\/actions\/runs\/(\d+)\/jobs$/.exec(parsed.pathname);
      if(!matched)throw new Error('Unexpected network path: '+parsed.pathname);
      assert.equal(parsed.searchParams.get('filter'),'latest');
      const list=data.jobs[Number(matched[1])]??[];
      value={total_count:data.jobCount??list.length,jobs:list};
    }
    return {
      ok:true,status:200,async json(){return structuredClone(value);}
    };
  };
  return {fetchImpl,requests};
}

test('exact successful main SHA requires 5 build jobs plus browser audit',async()=>{
  const calls=mock(fixtures());
  const report=await verifyExactMainCi(sha,{fetchImpl:calls.fetchImpl});
  assert.equal(report.verified,true);
  assert.equal(report.main_sha,sha);
  assert.equal(report.evidence.length,2);
  assert.equal(report.blocked.length,0);
  assert.deepEqual(report.evidence.map(v=>v.run_id),[201,202]);
  assert.equal(report.evidence[0].required_jobs.length,5);
  assert.equal(report.evidence[1].required_jobs.length,1);
  assert.equal(calls.requests.length,4);
  assert.ok(calls.requests.every(x=>x.url.startsWith('https://api.github.com/')));
});

test('a previously green run cannot hide a more recent failed run',async()=>{
  const data=fixtures();
  data.runs.push(successfulRun(expectedNames[0],203,{conclusion:'failure'}));
  const report=await verifyExactMainCi(sha,{fetchImpl:mock(data).fetchImpl});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.some(x=>
    x.gate===expectedNames[0]&&x.reason==='latest_run_not_successful'));
});

test('refuses green pull-request runs as production-main evidence',async()=>{
  const data=fixtures();
  data.runs=data.runs.map(x=>({...x,event:'pull_request',head_branch:'feature/hub'}));
  const report=await verifyExactMainCi(sha,{fetchImpl:mock(data).fetchImpl});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.some(x=>x.reason==='exact_sha_main_push_missing'));
});

test('refuses mismatched main commit and exact-head job mismatch',async()=>{
  const data=fixtures();
  data.mainSha=otherSha;
  data.jobs[201][0].head_sha=otherSha;
  const report=await verifyExactMainCi(sha,{fetchImpl:mock(data).fetchImpl});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.some(x=>x.reason==='candidate_not_current_main'));
  assert.ok(report.blocked.some(x=>x.reason==='missing_or_failed_required_job'));
});

test('refuses missing required jobs even if the workflow declares success',async()=>{
  const data=fixtures();
  data.jobs[201]=data.jobs[201].filter(x=>x.name!=='Mobile identity with PostgreSQL');
  const report=await verifyExactMainCi(sha,{fetchImpl:mock(data).fetchImpl});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.some(x=>x.job==='Mobile identity with PostgreSQL'));
});

test('rejects duplicate jobs and cancelled unrelated jobs',async()=>{
  const data=fixtures();
  data.jobs[201].push(structuredClone(data.jobs[201][0]));
  data.jobs[201].push({
    id:9001,name:'Optional job',status:'completed',conclusion:'cancelled'
  });
  const report=await verifyExactMainCi(sha,{fetchImpl:mock(data).fetchImpl});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.some(x=>x.reason==='other_failed_or_cancelled_job'));
  assert.ok(report.blocked.some(x=>x.job==='ASP.NET Core API'));
});

test('rejects incomplete pagination and missing browser workflow',async()=>{
  const data=fixtures();
  data.runCount=101;
  data.runs=data.runs.filter(x=>x.name===expectedNames[0]);
  const report=await verifyExactMainCi(sha,{fetchImpl:mock(data).fetchImpl});
  assert.equal(report.verified,false);
  assert.ok(report.blocked.some(x=>x.reason==='unverified_run_pagination'));
  assert.ok(report.blocked.some(x=>x.gate===expectedNames[1]));
});

test('rejects foreign repository payload and non-success network responses',async()=>{
  const data=fixtures();
  data.runs[0].repository.full_name='untrusted/repo';
  const report=await verifyExactMainCi(sha,{fetchImpl:mock(data).fetchImpl});
  assert.equal(report.verified,false);
  await assert.rejects(
    ()=>verifyExactMainCi(sha,{fetchImpl:async()=>({
      ok:false,status:403,json:async()=>({message:'Forbidden'})
    })}),
    /evidence unavailable/);
});

test('validates candidate SHA and token before any external request',async()=>{
  let calls=0;
  const fetchImpl=async()=>{calls++;throw new Error('Must not be called');};
  for(const candidate of ['',otherSha.toUpperCase(),'abc','x'.repeat(40)]){
    await assert.rejects(()=>verifyExactMainCi(candidate,{fetchImpl}),/Git SHA/);
  }
  await assert.rejects(()=>verifyExactMainCi(sha,{
    fetchImpl,token:'untrusted\r\nhost: elsewhere'
  }),/token/);
  assert.equal(calls,0);
});

test('exact-main release browser coverage runs for every main push, even backend-only',()=>{
  const workflow=readFileSync(
    new URL('../../.github/workflows/browser-ui-visual-audit.yml',import.meta.url),
    'utf8');
  assert.match(workflow,/on:\s*\n\s*#.*\n\s*#.*\n\s*push:\s*\n\s*branches: \[main\]/);
  assert.match(workflow,/pull_request:\s*\n\s*branches: \[main, 'rewrite\/\*\*', 'feat\/\*\*', 'fix\/\*\*'\]\s*\n\s*paths:/);
  // The browser audit on main must not have a paths filter;
  // otherwise backend-only release candidates have no exact-SHA proof.
  const push=workflow.slice(workflow.indexOf('  push:'),workflow.indexOf('  pull_request:'));
  assert.doesNotMatch(push,/paths:/);
  assert.match(workflow,/workflow_dispatch:/);
});

test('manual release audit has no deployments or privileged environment credentials',()=>{
  const workflow=readFileSync(
    new URL('../../.github/workflows/production-release-preflight.yml',import.meta.url),
    'utf8');
  assert.match(workflow,/permissions:\s*\n\s*contents: read\s*\n\s*actions: read/);
  assert.match(workflow,/persist-credentials: false/);
  assert.match(workflow,/ref: main/);
  assert.match(workflow,/verify-exact-ci[.]mjs/);
  assert.match(workflow,/verify-release-authorization[.]mjs/);
  assert.doesNotMatch(workflow,/^\s*environment:\s*(?:production|live)\s*$/m);
  assert.doesNotMatch(workflow,/^\s*-\s*run:\s*(?:docker\s+compose\s+up|ssh|scp|kubectl|helm)/m);
});

test('rewrite CI covers stacked branches without widening main push or permissions',()=>{
  const workflow=readFileSync(
    new URL('../../.github/workflows/rewrite-build.yml',import.meta.url),'utf8');
  assert.match(workflow,/push:\s*\n\s*branches:\s*\n\s*- main\s*\n\s*pull_request:/);
  assert.match(workflow,/pull_request:\s*\n\s*branches:\s*\n\s*- main\s*\n\s*- rewrite\/\*\*\s*\n\s*- feat\/\*\*\s*\n\s*- fix\/\*\*\s*\n\s*workflow_dispatch:/);
  assert.match(workflow,/permissions:\s*\n\s*contents: read\s*\n\s*concurrency:/);
  assert.doesNotMatch(workflow,/pull_request_target:/);
});
