#!/usr/bin/env node
/**
 * Read-only fail-closed GitHub main branch control check.
 * This checks the branch protection status and required CI contexts exposed by
 * the GitHub branches REST endpoint. It does not inspect bypass allowances or
 * independently prove required PR reviews: those still require operator audit.
 * Never connects to Hostinger, production APIs, databases or payment services.
 */
import process from 'node:process';
import {fileURLToPath} from 'node:url';

const REPOSITORY='MikeCy-com/Lefteria-FC-OS';
const BRANCH_URL='https://api.github.com/repos/'+REPOSITORY+'/branches/main';
const SHA=/^[0-9a-f]{40}$/;
export const REQUIRED_MAIN_CHECKS=Object.freeze([
  'ASP.NET Core API',
  'SvelteKit web',
  'Mobile identity with PostgreSQL',
  'API contract and startup smoke',
  'Docker Compose build',
  'Browser screenshots and viewport overflow'
]);

export async function verifyMainProtection(sha,{fetchImpl=fetch,token=''}={}) {
  if(typeof sha!=='string'||!SHA.test(sha))
    throw new Error('An exact lowercase 40-character main SHA is required.');
  if(typeof fetchImpl!=='function')throw new Error('GitHub reader unavailable.');
  if(typeof token!=='string'||token.length>1024||/[\r\n]/.test(token))
    throw new Error('Invalid GitHub token.');
  const response=await fetchImpl(BRANCH_URL,{
    method:'GET',redirect:'error',signal:AbortSignal.timeout(15000),
    headers:{
      Accept:'application/vnd.github+json',
      'X-GitHub-Api-Version':'2022-11-28',
      ...(token?{Authorization:'Bearer '+token}:{})
    }
  });
  if(!response||response.ok!==true)
    throw new Error('Branch governance evidence unavailable (HTTP '+
      String(response?.status??'unknown')+').');
  const branch=await response.json();
  if(!branch||typeof branch!=='object'||Array.isArray(branch))
    throw new Error('Invalid GitHub branch evidence.');
  const status=branch.protection?.required_status_checks;
  const contexts=new Set([
    ...(Array.isArray(status?.contexts)?status.contexts:[]),
    ...(Array.isArray(status?.checks)?
      status.checks.map(check=>check?.context):[])
  ].filter(item=>typeof item==='string'));
  const missing=REQUIRED_MAIN_CHECKS.filter(name=>!contexts.has(name));
  const blocked=[];
  if(branch.name!=='main'||branch.commit?.sha!==sha)
    blocked.push('not_exact_current_main');
  if(branch.protected!==true||branch.protection?.enabled!==true)
    blocked.push('main_branch_not_protected');
  if(status?.enforcement_level!=='everyone')
    blocked.push('status_checks_not_enforced_for_everyone');
  if(missing.length)blocked.push('required_check_contexts_missing');
  return {
    repository:REPOSITORY,sha,verified:blocked.length===0,
    blocked,missing_required_checks:missing,
    observed_protected:branch.protected===true,
    observed_status_enforcement:status?.enforcement_level??null,
    caveat:'PR review, ruleset bypass and administrator permissions require separate operator verification; no release or deployment authorized.'
  };
}

async function main(args=process.argv.slice(2)){
  const shas=args.filter(value=>value.startsWith('--sha='));
  if(shas.length!==1||args.some(value=>!value.startsWith('--sha=')&&value!=='--require-passing')){
    console.error('Usage: verify-main-protection.mjs --sha=<current-main-sha> [--require-passing]');
    process.exitCode=2;return;
  }
  try{
    const report=await verifyMainProtection(shas[0].slice(6),{
      token:process.env.GITHUB_TOKEN??''
    });
    console.log('Main branch required CI governance:',report.verified?'PASS':'BLOCKED');
    console.log('Candidate:',report.sha);
    for(const reason of report.blocked)console.log('BLOCKED',reason);
    for(const name of report.missing_required_checks)console.log('MISSING CHECK',name);
    console.log(report.caveat);
    if(args.includes('--require-passing')&&!report.verified)process.exitCode=1;
  }catch(error){
    console.error('Branch governance unavailable:',error instanceof Error?error.message:'unknown error');
    process.exitCode=1;
  }
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1])
  await main();
