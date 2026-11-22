#!/usr/bin/env node
/**
 * The final read-only GO/NO-GO decision guard, separate from the planning
 * checklist. No infrastructure, customer data, providers, migrations or DNS
 * are accessed. `--require-ready` MUST independently read GitHub Actions.
 */
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import process from 'node:process';
import {checkReleaseReadiness} from './check-go-live.mjs';
import {verifyExactMainCi,RELEASE_REPOSITORY,REQUIRED_WORKFLOWS}
  from './verify-exact-ci.mjs';

const SHA=/^[a-f0-9]{40}$/;
const PROFILES=new Set(['full','academy-mvp']);
const SIGNOFF_ROLES=Object.freeze([
  'club_owner','finance_officer','privacy_officer',
  'infrastructure_operator','hub_editor'
]);
function validDate(value){
  if(typeof value!=='string'||!/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value))
    return false;
  const date=new Date(value+'T00:00:00Z');
  return !Number.isNaN(date.valueOf())&&
    date.toISOString().slice(0,10)===value&&
    value<=new Date().toISOString().slice(0,10);
}
function validProofUrl(value){
  if(typeof value!=='string'||value.length<12||value.length>2048)
    return false;
  try{
    const url=new URL(value);
    return url.protocol==='https:'&&url.username===''&&url.password===''&&
      url.hash===''&&url.hostname.length>3&&
      !/(^localhost$|^127[.]|[.]invalid$|^example[.])/.test(url.hostname);
  }catch{return false;}
}
export function matchesIndependentCi(ci,sha){
  if(!ci||ci.verified!==true||ci.source!=='github-actions-rest'||
     ci.repository!==RELEASE_REPOSITORY||ci.candidate_sha!==sha||
     ci.main_sha!==sha||!Array.isArray(ci.blocked)||ci.blocked.length!==0||
     !Array.isArray(ci.evidence)||
     ci.evidence.length!==Object.keys(REQUIRED_WORKFLOWS).length)
    return false;
  return Object.entries(REQUIRED_WORKFLOWS).every(([workflow,required])=>
    ci.evidence.some(item=>item?.workflow===workflow&&item.sha===sha&&
      Number.isSafeInteger(item.run_id)&&item.run_id>0&&
      item.url==='https://github.com/'+RELEASE_REPOSITORY+
        '/actions/runs/'+item.run_id&&
      Array.isArray(item.required_jobs)&&
      item.required_jobs.length===required.length&&
      required.every(job=>item.required_jobs.includes(job))));
}

export function hasOperatorApproval(approval,sha,profile){
  if(!approval||typeof approval!=='object'||Array.isArray(approval)||
     approval.schema_version!==1||approval.scope!==profile||
     approval.status!=='approved'||approval.candidate_sha!==sha||
     !validDate(approval.decision_at)||
     !validProofUrl(approval.change_ticket)||
     !Array.isArray(approval.signoffs)||
     approval.signoffs.length!==SIGNOFF_ROLES.length)return false;
  const roles=new Set();
  const identities=new Set();
  const evidenceUrls=new Set();
  for(const signer of approval.signoffs){
    if(!signer||!SIGNOFF_ROLES.includes(signer.role)||roles.has(signer.role)||
       typeof signer.name!=='string'||
       !/^[\p{L}0-9 ._@-]{3,120}$/u.test(signer.name)||
       !validDate(signer.signed_at)||
       signer.signed_at>approval.decision_at||
       !validProofUrl(signer.evidence_url))return false;
    // Distinct role strings alone never prove five independent reviewers.
    // Normalize harmless case/spacing variations before comparing identities.
    const identity=signer.name.normalize('NFKC').trim()
      .replace(/\s+/g,' ').toLowerCase();
    if(identities.has(identity)||evidenceUrls.has(signer.evidence_url))
      return false;
    roles.add(signer.role);
    identities.add(identity);
    evidenceUrls.add(signer.evidence_url);
  }
  if(roles.size!==SIGNOFF_ROLES.length||
     identities.size!==SIGNOFF_ROLES.length||
     evidenceUrls.size!==SIGNOFF_ROLES.length)return false;
  // Operator names and evidence links are an audit aid, not proof of real-world independence.
  return true;
}

export function checkProductionAuthorization({
  manifest,approval,ci,sha,profile='academy-mvp'
}){
  if(!PROFILES.has(profile))throw new Error('Unsupported release scope.');
  if(typeof sha!=='string'||!SHA.test(sha))
    throw new Error('An exact lowercase 40-character release SHA is required.');
  const plan=checkReleaseReadiness(manifest,sha,profile);
  const blocked=[...plan.blocked];
  if(profile==='academy-mvp'&&plan.profile!=='academy-mvp'){
    blocked.push({id:'academy-mvp-profile',priority:'P0',
      reason:'MVP release profile not yet available in main'});
  }
  if(!matchesIndependentCi(ci,sha)){
    blocked.push({id:'independent-main-ci',priority:'P0',
      reason:'Required exact-main-SHA build and browser jobs unverified'});
  }
  if(!hasOperatorApproval(approval,sha,profile)){
    blocked.push({id:'operator-go-decision',priority:'P0',
      reason:'Five named owner/finance/privacy/infrastructure/HUB signoffs missing'});
  }
  return {
    ready:blocked.length===0&&plan.ready===true,
    scope:profile,candidate_sha:sha,
    exact_ci_verified:matchesIndependentCi(ci,sha),
    operator_approved:hasOperatorApproval(approval,sha,profile),
    blocked_count:blocked.length,blocked
  };
}
async function main(){
  const args=process.argv.slice(2);
  const shas=args.filter(x=>x.startsWith('--sha='));
  const profiles=args.filter(x=>x.startsWith('--profile='));
  const requireReady=args.includes('--require-ready');
  if(shas.length!==1||profiles.length>1||
     args.some(x=>x!=='--require-ready'&&!x.startsWith('--sha=')&&
       !x.startsWith('--profile='))){
    console.error('Usage: verify-release-authorization.mjs --sha=<current-main-sha> [--profile=academy-mvp|full] [--require-ready]');
    process.exitCode=2;return;
  }
  try{
    const sha=shas[0].slice(6);
    const profile=profiles.length?profiles[0].slice(10):'academy-mvp';
    const manifest=JSON.parse(readFileSync(
      new URL('./go-live-gates.json',import.meta.url),'utf8'));
    const approval=JSON.parse(readFileSync(
      new URL('./operator-release-approval.json',import.meta.url),'utf8'));
    // Do NOT accept a local user-provided CI report: a forged JSON file must
    // never satisfy the production release gate. Only GitHub REST is used.
    const ci=requireReady?await verifyExactMainCi(sha,{
      token:process.env.GITHUB_TOKEN??''
    }):null;
    const result=checkProductionAuthorization({
      manifest,approval,ci,sha,profile});
    console.log('Production release GO/NO-GO:',result.ready?'GO':'NO-GO');
    console.log('Scope:',result.scope,'candidate:',result.candidate_sha);
    console.log('Independent exact-main CI:',result.exact_ci_verified?'PASS':'NOT VERIFIED');
    console.log('Human release signoff:',result.operator_approved?'RECORDED':'MISSING');
    console.log('Remaining requirements:',result.blocked_count);
    for(const item of result.blocked)
      console.log(item.priority,item.id+':',item.reason);
    console.log('Read-only verification; this command NEVER deploys.');
    if(requireReady&&!result.ready)process.exitCode=1;
  }catch(err){
    console.error('Release authorization blocked:',
      err instanceof Error?err.message:'unknown error');
    process.exitCode=1;
  }
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1])
  await main();
