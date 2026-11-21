import test from 'node:test';
import assert from 'node:assert/strict';
import { checkReleaseReadiness } from './check-go-live.mjs';
import { readFileSync } from 'node:fs';

const productionSha='a'.repeat(40);
const gates=JSON.parse(readFileSync(new URL('./go-live-gates.json',import.meta.url),'utf8'));
const clone=value=>structuredClone(value);

test('actual development snapshot blocks all deployment or cutover',()=>{
  const report=checkReleaseReadiness(gates,productionSha);
  assert.equal(report.ready,false);
  assert.equal(report.blocked_count,gates.gates.length);
  assert.ok(report.blocked.some(x=>x.id==='data-import'));
  assert.ok(report.blocked.some(x=>x.id==='api-ci'));
  assert.ok(report.blocked.some(x=>x.id==='finance-reconciliation'));
});

test('verified status without evidence or exact candidate SHA remains blocked',()=>{
  const sample=clone(gates);
  for(const gate of sample.gates) {
    gate.status='verified';gate.release_sha=productionSha;
    gate.verified_at='2026-11-09';gate.evidence=[];
  }
  let result=checkReleaseReadiness(sample,productionSha);
  assert.equal(result.ready,false);
  assert.equal(result.blocked_count,sample.gates.length);
  for(const gate of sample.gates)gate.evidence=['https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/123'];
  result=checkReleaseReadiness(sample,'b'.repeat(40));
  assert.equal(result.ready,false);
  result=checkReleaseReadiness(sample);
  assert.equal(result.ready,false);
  assert.ok(result.blocked.some(gate=>gate.id==='candidate-release-sha'));
  result=checkReleaseReadiness(sample,productionSha);
  assert.equal(result.ready,true);
});

test('missing or duplicate core controls and unknown status fail closed',()=>{
  const omitted=clone(gates);
  omitted.gates=omitted.gates.filter(g=>g.id!=='privacy-security');
  assert.throws(()=>checkReleaseReadiness(omitted));
  const duplicate=clone(gates);
  duplicate.gates.push(clone(duplicate.gates[0]));
  assert.throws(()=>checkReleaseReadiness(duplicate));
  const unknown=clone(gates);
  unknown.gates[0].status='looks_ready';
  assert.throws(()=>checkReleaseReadiness(unknown));
  assert.throws(()=>checkReleaseReadiness(gates,'not-a-sha'));
});
