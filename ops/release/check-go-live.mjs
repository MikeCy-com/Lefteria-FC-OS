#!/usr/bin/env node
/**
 * Non-deploying OS release readiness guard.
 *
 * Usage:
 *   node ops/release/check-go-live.mjs
 *   node ops/release/check-go-live.mjs --require-ready --sha=<exact 40-char candidate SHA>
 *
 * A passing code build or a live-looking admin page NEVER marks a gate verified.
 * The report is independent of production; this tool does not connect to hosts.
 */
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import process from 'node:process';

const RELEASE_SHA=/^[0-9a-f]{40}$/;
const RELEASE_STATUSES=new Set(['blocked','in_progress','verified']);
const SOURCE_REPO='MikeCy-com/Lefteria-FC-OS';

export function checkReleaseReadiness(input, targetSha=null) {
  if(!input || typeof input!=='object' || Array.isArray(input) ||
    input.schema_version!==1 || input.target_repository!==SOURCE_REPO ||
    !Array.isArray(input.gates) || input.gates.length<1) {
    throw new Error('Invalid release gate document; fail closed.');
  }
  if(targetSha!==null && !RELEASE_SHA.test(targetSha))
    throw new Error('A verified 40-character commit SHA is required.');
  const ids=new Set();
  const reasons=[];
  for(const gate of input.gates) {
    if(!gate || typeof gate!=='object'||Array.isArray(gate)||
      typeof gate.id!=='string'|| !/^[a-z0-9-]{2,64}$/.test(gate.id) ||
      typeof gate.description!=='string'|| gate.description.length<5 ||
      !['P0','P1'].includes(gate.priority)||
      !RELEASE_STATUSES.has(gate.status)||
      !Array.isArray(gate.evidence)||gate.evidence.length>25 ||
      gate.evidence.some(item=>typeof item!=='string' || item.length<12||item.length>2048)||
      ids.has(gate.id)) throw new Error('Invalid or duplicate gate record; fail closed.');
    ids.add(gate.id);
    if(gate.status!=='verified') {
      reasons.push({id:gate.id,priority:gate.priority,reason:gate.status});
      continue;
    }
    if(!RELEASE_SHA.test(gate.release_sha??'') ||
      (targetSha!==null && gate.release_sha!==targetSha) ||
      !/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/.test(gate.verified_at??'') ||
      gate.evidence.length<1) {
      reasons.push({id:gate.id,priority:gate.priority,reason:'missing_or_wrong_verification_evidence'});
    }
  }
  if(!ids.has('api-ci') || !ids.has('data-import') ||
    !ids.has('privacy-security')|| !ids.has('finance-reconciliation')||
    !ids.has('cutover-rollback')) throw new Error('Core release gates missing; fail closed.');
  return {ready:reasons.length===0,gate_count:ids.size,
    blocked_count:reasons.length,blocked:reasons};
}

function main() {
  const args=process.argv.slice(2);
  const requireReady=args.includes('--require-ready');
  const requested=args.filter(x=>x.startsWith('--sha='));
  if(args.some(x=>x!=='--require-ready'&&!x.startsWith('--sha='))||
    requested.length>1 || (requireReady&&requested.length!==1)) {
    console.error('Usage: check-go-live.mjs [--require-ready --sha=<commit_sha>]');
    process.exitCode=2;
    return;
  }
  const expectedSha=requested.length?requested[0].slice(6):null;
  const data=JSON.parse(readFileSync(
    new URL('./go-live-gates.json',import.meta.url),'utf8'));
  const report=checkReleaseReadiness(data,expectedSha);
  console.log('Lefteria FC OS readiness:',report.ready?'VERIFIED':'BLOCKED');
  console.log('Audited gates:',report.gate_count,'Unverified:',report.blocked_count);
  for(const item of report.blocked)
    console.log(item.priority,item.id+':',item.reason);
  console.log('This report performs no deployment and is not proof of a VPS release.');
  if(requireReady&&!report.ready)process.exitCode=1;
}

if(process.argv[1] && fileURLToPath(import.meta.url)===process.argv[1])
  main();
