#!/usr/bin/env node
/**
 * Read-only, exact-main-SHA GitHub Actions verification for a release candidate.
 *
 * CI evidence is NOT proof of provider activation, data reconciliation,
 * backup/restore or operational approval. This tool never touches a VPS,
 * database, DNS, checkout, credentials or deployment destination.
 */
import {writeFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import process from 'node:process';

export const RELEASE_REPOSITORY='MikeCy-com/Lefteria-FC-OS';
export const REQUIRED_WORKFLOWS=Object.freeze({
  'Rewrite build':Object.freeze([
    'ASP.NET Core API','SvelteKit web',
    'Mobile identity with PostgreSQL','API contract and startup smoke',
    'Docker Compose build'
  ]),
  'Browser UI visual audit':Object.freeze([
    'Browser screenshots and viewport overflow'
  ])
});
const SHA=/^[0-9a-f]{40}$/;
const API='https://api.github.com/repos/'+RELEASE_REPOSITORY;

function assertSha(value) {
  if(typeof value!=='string'||!SHA.test(value))
    throw new Error('A lowercase, complete 40-character Git SHA is required.');
  return value;
}

async function request(path,fetchImpl,token) {
  // A caller cannot supply an alternate API origin, path traversal or host.
  if(typeof path!=='string'||!path.startsWith('/')||path.includes('..')||
     path.includes('//')||path.includes('#'))
    throw new Error('Unexpected GitHub API path.');
  const response=await fetchImpl(API+path,{
    method:'GET',redirect:'error',signal:AbortSignal.timeout(15000),
    headers:{
      'Accept':'application/vnd.github+json',
      'X-GitHub-Api-Version':'2022-11-28',
      ...(token?{'Authorization':'Bearer '+token}:{})
    }
  });
  if(!response||response.ok!==true)
    throw new Error('GitHub Actions evidence unavailable (HTTP '+String(response?.status??'unknown')+').');
  const data=await response.json();
  if(!data||typeof data!=='object'||Array.isArray(data))
    throw new Error('Invalid GitHub API evidence.');
  return data;
}

function isRunForCandidate(run,sha,name) {
  return run&&typeof run==='object'&&!Array.isArray(run)&&
    run.name===name&&run.head_sha===sha&&run.event==='push'&&
    run.head_branch==='main'&&run.repository?.full_name===RELEASE_REPOSITORY&&
    Number.isSafeInteger(run.id)&&run.id>0;
}

export async function verifyExactMainCi(sha,{
  fetchImpl=fetch,token='',requireMainHead=true
}={}) {
  assertSha(sha);
  if(typeof fetchImpl!=='function')throw new Error('GitHub request reader required.');
  if(typeof token!=='string'||token.length>1024||/[\r\n]/.test(token))
    throw new Error('Invalid GitHub token environment value.');
  const blocked=[];
  const evidence=[];
  let mainSha=null;
  if(requireMainHead) {
    const branch=await request('/branches/main',fetchImpl,token);
    mainSha=branch.commit?.sha??null;
    if(mainSha!==sha)blocked.push({
      gate:'candidate-main',reason:'candidate_not_current_main'
    });
  }
  const data=await request(
    '/actions/runs?head_sha='+sha+'&per_page=100',fetchImpl,token);
  if(!Array.isArray(data.workflow_runs)||
     !Number.isSafeInteger(data.total_count)||data.total_count<0)
    throw new Error('GitHub Actions run list malformed.');
  // Pagination is intentionally fail-closed. Do not silently miss a newer run.
  if(data.total_count>data.workflow_runs.length)
    blocked.push({gate:'actions-run-list',reason:'unverified_run_pagination'});

  for(const [name,expectedJobs] of Object.entries(REQUIRED_WORKFLOWS)){
    const candidates=data.workflow_runs
      .filter(run=>isRunForCandidate(run,sha,name))
      .sort((a,b)=>b.id-a.id);
    const latest=candidates[0];
    if(!latest){
      blocked.push({gate:name,reason:'exact_sha_main_push_missing'});
      continue;
    }
    if(latest.status!=='completed'||latest.conclusion!=='success'){
      blocked.push({gate:name,reason:'latest_run_not_successful'});
      continue;
    }
    const jobs=await request(
      '/actions/runs/'+latest.id+'/jobs?per_page=100&filter=latest',
      fetchImpl,token);
    if(!Array.isArray(jobs.jobs)||
       !Number.isSafeInteger(jobs.total_count)||jobs.total_count<0)
      throw new Error('GitHub Actions job list malformed.');
    if(jobs.total_count>jobs.jobs.length){
      blocked.push({gate:name,reason:'unverified_job_pagination'});
      continue;
    }
    let valid=true;
    for(const expected of expectedJobs){
      const matches=jobs.jobs.filter(job=>job?.name===expected);
      if(matches.length!==1||matches[0].status!=='completed'||
         matches[0].conclusion!=='success'||
         (matches[0].head_sha&&matches[0].head_sha!==sha)){
        blocked.push({gate:name,reason:'missing_or_failed_required_job',job:expected});
        valid=false;
      }
    }
    if(jobs.jobs.some(job=>job?.conclusion==='failure'||
       job?.conclusion==='cancelled'||job?.conclusion==='timed_out')){
      blocked.push({gate:name,reason:'other_failed_or_cancelled_job'});
      valid=false;
    }
    if(valid)evidence.push({
      workflow:name,run_id:latest.id,
      sha,url:'https://github.com/'+RELEASE_REPOSITORY+
        '/actions/runs/'+latest.id,
      required_jobs:[...expectedJobs]
    });
  }
  return {
    schema_version:1,source:'github-actions-rest',
    repository:RELEASE_REPOSITORY,candidate_sha:sha,
    main_sha:mainSha,verified:blocked.length===0,
    evidence,blocked
  };
}

async function main(args=process.argv.slice(2)){
  const candidate=args.filter(x=>x.startsWith('--sha='));
  const output=args.filter(x=>x.startsWith('--output='));
  const allowed=args.every(x=>x==='--require-passing'||
    x.startsWith('--sha=')||x.startsWith('--output='));
  if(!allowed||candidate.length!==1||output.length>1||
     (output.length===1&&output[0].slice(9).length===0)){
    console.error('Usage: verify-exact-ci.mjs --sha=<40-char-main-sha> [--output=<report-file>] [--require-passing]');
    process.exitCode=2;
    return;
  }
  try{
    const report=await verifyExactMainCi(candidate[0].slice(6),{
      token:process.env.GITHUB_TOKEN??''
    });
    if(output.length)writeFileSync(output[0].slice(9),
      JSON.stringify(report,null,2)+'\n',{flag:'w',mode:0o600});
    console.log('Exact GitHub CI:',report.verified?'PASS':'BLOCKED');
    console.log('Release candidate:',report.candidate_sha);
    for(const entry of report.evidence)
      console.log('PASS',entry.workflow,entry.url);
    for(const entry of report.blocked)
      console.log('BLOCKED',entry.gate,entry.reason,entry.job??'');
    console.log('No deployment performed. CI success is not go-live authorization.');
    if(args.includes('--require-passing')&&!report.verified)
      process.exitCode=1;
  }catch(error){
    console.error('CI verification unavailable:',error instanceof Error?error.message:'unknown error');
    process.exitCode=1;
  }
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1])
  await main();
