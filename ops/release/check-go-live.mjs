#!/usr/bin/env node
/**
 * Non-deploying Lefteria FC OS release readiness guard.
 *
 * A clean build alone never authorizes a production cutover.
 * --require-ready independently checks GitHub's exact-main-SHA jobs;
 * it never trusts a user-supplied CI report. No Hostinger, Payabl, Twilio,
 * production secrets, databases, DNS, or runtime service is modified.
 */
import {readFileSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import process from 'node:process';
import {verifyExactMainCi,RELEASE_REPOSITORY,REQUIRED_WORKFLOWS}
  from './verify-exact-ci.mjs';

const SHA=/^[0-9a-f]{40}$/;
const STATUS=new Set(['blocked','in_progress','verified']);
const CORE=new Set([
  'api-ci','data-import','privacy-security','finance-reconciliation',
  'cutover-rollback','backups-restore','role-mobile-parity','hub-education'
]);
function validDate(value){
  return typeof value==='string'&&/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(value)&&
    !Number.isNaN(Date.parse(value+'T00:00:00Z'))&&
    new Date(value+'T00:00:00Z').toISOString().slice(0,10)===value;
}
function validEvidence(value){
  if(typeof value!=='string'||value.length<12||value.length>2048)return false;
  try{
    const url=new URL(value);
    return url.protocol==='https:'&&url.username===''&&url.password===''&&
      url.hostname.length>3&&url.hash===''&&
      !/(?:example[.](?:com|org|invalid)|localhost|127[.]0[.]0[.]1)$/i.test(url.hostname);
  }catch{return false;}
}
function verifiedCiReport(value,sha){
  if(!value||value.verified!==true||value.repository!==RELEASE_REPOSITORY||
     value.source!=='github-actions-rest'||value.candidate_sha!==sha||
     value.main_sha!==sha||!Array.isArray(value.evidence)||
     value.evidence.length!==Object.keys(REQUIRED_WORKFLOWS).length||
     !Array.isArray(value.blocked)||value.blocked.length!==0)return false;
  return Object.entries(REQUIRED_WORKFLOWS).every(([name,jobs])=>
    value.evidence.some(e=>e.workflow===name&&
      e.sha===sha&&Number.isSafeInteger(e.run_id)&&e.run_id>0&&
      e.url==='https://github.com/'+RELEASE_REPOSITORY+
        '/actions/runs/'+e.run_id&&
      Array.isArray(e.required_jobs)&&
      jobs.every(job=>e.required_jobs.includes(job))));
}

/**
 * Readiness is a conjunction of independently verified exact-SHA CI,
 * operator-attested release decision and every mandatory critical gate.
 * A manifest alone, with arbitrary green links, can NEVER make ready=true.
 */
export function checkReleaseReadiness(input,targetSha=null,ciReport=null){
  if(!input||typeof input!=='object'||Array.isArray(input)||
     input.schema_version!==1||input.target_repository!==RELEASE_REPOSITORY||
     !Array.isArray(input.gates)||input.gates.length<1||
     input.default_release_status!=='BLOCKED')
    throw new Error('Invalid release gate document; fail closed.');
  if(targetSha!==null&&!SHA.test(targetSha))
    throw new Error('A verified 40-character commit SHA is required.');

  const ids=new Set();
  const reasons=[];
  const byId=new Map();
  for(const gate of input.gates){
    if(!gate||typeof gate!=='object'||Array.isArray(gate)||
       typeof gate.id!=='string'||!/^[a-z0-9-]{2,64}$/.test(gate.id)||
       typeof gate.description!=='string'||gate.description.length<5||
       !['P0','P1'].includes(gate.priority)||
       !STATUS.has(gate.status)||!Array.isArray(gate.evidence)||
       gate.evidence.length>25||gate.evidence.some(x=>!validEvidence(x))||
       ids.has(gate.id))
      throw new Error('Invalid or duplicate release gate record; fail closed.');
    if(gate.requires!==undefined&&
       (!Array.isArray(gate.requires)||gate.requires.length>25||
        gate.requires.some(x=>typeof x!=='string'||
          !/^[a-z0-9-]{2,64}$/.test(x))||
        new Set(gate.requires).size!==gate.requires.length||
        gate.requires.includes(gate.id)))
      throw new Error('Invalid release gate dependency.');
    ids.add(gate.id);
    byId.set(gate.id,gate);
    if(gate.status!=='verified'){
      reasons.push({id:gate.id,priority:gate.priority,reason:gate.status});
      continue;
    }
    if(!SHA.test(gate.release_sha??'')||
       (targetSha!==null&&gate.release_sha!==targetSha)||
       !validDate(gate.verified_at)||gate.evidence.length<1)
      reasons.push({id:gate.id,priority:gate.priority,
        reason:'missing_or_wrong_verification_evidence'});
  }
  for(const id of CORE)
    if(!ids.has(id))throw new Error('Mandatory '+id+' release gate missing.');
  for(const gate of input.gates){
    for(const dependency of gate.requires??[]){
      if(!byId.has(dependency))
        throw new Error('Unknown release gate dependency: '+dependency);
      if(gate.status==='verified'&&byId.get(dependency).status!=='verified')
        reasons.push({id:gate.id,priority:gate.priority,
          reason:'required_gate_not_verified:'+dependency});
    }
  }

  if(targetSha===null)reasons.push({
    id:'candidate-release-sha',priority:'P0',reason:'exact_release_sha_required'
  });
  if(!verifiedCiReport(ciReport,targetSha))reasons.push({
    id:'independent-ci',priority:'P0',
    reason:'exact_main_sha_github_actions_verification_required'
  });
  // Explicit human release approval is separate from CI and code review.
  // The default manifest has no approved decision; never self-authorize.
  const d=input.release_decision;
  if(!d||d.status!=='approved'||d.scope!=='full-production'||
     d.release_sha!==targetSha||
     !validDate(d.approved_at)||
     typeof d.approved_by!=='string'||
     !/^[A-Za-z0-9 ._@-]{3,120}$/.test(d.approved_by)||
     !validEvidence(d.ticket_url))
    reasons.push({id:'operator-release-decision',priority:'P0',
      reason:'documented_operator_approval_required'});

  return {ready:reasons.length===0,gate_count:ids.size,
    blocked_count:reasons.length,blocked:reasons,
    exact_ci_verified:verifiedCiReport(ciReport,targetSha)};
}

async function main(){
  const args=process.argv.slice(2);
  const requireReady=args.includes('--require-ready');
  const requested=args.filter(x=>x.startsWith('--sha='));
  if(args.some(x=>x!=='--require-ready'&&!x.startsWith('--sha='))||
     requested.length>1||(requireReady&&requested.length!==1)){
    console.error('Usage: check-go-live.mjs [--require-ready --sha=<40-char-main-sha>]');
    process.exitCode=2;
    return;
  }
  try{
    const expectedSha=requested.length?requested[0].slice(6):null;
    let ci=null;
    // Never accept a CI JSON file/flag as proof. Use independent GitHub
    // API verification each time an operator requests readiness.
    if(requireReady)ci=await verifyExactMainCi(expectedSha,{
      token:process.env.GITHUB_TOKEN??''
    });
    const manifest=JSON.parse(readFileSync(
      new URL('./go-live-gates.json',import.meta.url),'utf8'));
    const report=checkReleaseReadiness(manifest,expectedSha,ci);
    console.log('Lefteria FC OS readiness:',report.ready?'RELEASE GATES SATISFIED':'BLOCKED');
    console.log('Audited gates:',report.gate_count,'Blocked requirements:',report.blocked_count);
    console.log('Independent exact-main-SHA CI:',report.exact_ci_verified?'verified':'unverified');
    for(const item of report.blocked)
      console.log(item.priority,item.id+':',item.reason);
    console.log('Read-only readiness report; no production state changed.');
    if(requireReady&&!report.ready)process.exitCode=1;
  }catch(error){
    console.error('Release verification failed closed:',
      error instanceof Error?error.message:'unknown error');
    process.exitCode=1;
  }
}

if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1])
  await main();
