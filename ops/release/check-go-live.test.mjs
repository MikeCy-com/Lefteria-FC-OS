import test from 'node:test';
import assert from 'node:assert/strict';
import { checkReleaseReadiness, MVP_REQUIRED_GATES } from './check-go-live.mjs';
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

test('Academy MVP cannot be approved without all critical identity, finance and recovery gates',()=>{
  const report=checkReleaseReadiness(gates,productionSha,'academy-mvp');
  assert.equal(report.ready,false);
  assert.equal(report.profile,'academy-mvp');
  assert.equal(report.gate_count,MVP_REQUIRED_GATES.length);
  assert.ok(report.blocked.some(g=>g.id==='identity-delivery'));
  assert.ok(report.blocked.some(g=>g.id==='mvp-deferred-features'));
  assert.ok(report.blocked.some(g=>g.id==='mvp-public-essentials'));
  assert.ok(report.blocked.some(g=>g.id==='backups-restore'));
  assert.ok(report.blocked.some(g=>g.id==='cutover-rollback'));
  assert.ok(report.blocked.some(g=>g.id==='finance-reconciliation'));
  assert.ok(!report.blocked.some(g=>g.id==='pos-payments-unified'));
  assert.ok(!report.blocked.some(g=>g.id==='communications'));
  assert.ok(!report.blocked.some(g=>g.id==='cms-publishing'));
});
test('a narrower MVP requires an exact reviewed SHA and verifiable evidence',()=>{
  const data=clone(gates);
  for(const gate of data.gates){
    if(!MVP_REQUIRED_GATES.includes(gate.id))continue;
    gate.status='verified';
    gate.release_sha=productionSha;
    gate.verified_at='2026-11-21';
    gate.evidence=['https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/123'];
  }
  assert.equal(checkReleaseReadiness(data,productionSha,'academy-mvp').ready,true);
  assert.equal(checkReleaseReadiness(data,productionSha,'full').ready,false);
  const withoutSha=checkReleaseReadiness(data,null,'academy-mvp');
  assert.equal(withoutSha.ready,false);
  assert.ok(withoutSha.blocked.some(g=>g.id==='candidate-release-sha'));
  assert.equal(checkReleaseReadiness(data,'b'.repeat(40),'academy-mvp').ready,false);
  const missing=clone(data);
  missing.gates=missing.gates.filter(g=>g.id!=='identity-delivery');
  assert.throws(()=>checkReleaseReadiness(missing,productionSha,'academy-mvp'));
  assert.throws(()=>checkReleaseReadiness(data,productionSha,'unsafe-shortcut'));
  const notDisabled=clone(data);
  notDisabled.gates.find(g=>g.id==='mvp-deferred-features').status='blocked';
  assert.equal(checkReleaseReadiness(notDisabled,productionSha,'academy-mvp').ready,false);
});

test('verified release gates reject invalid or future date and fabricated evidence URLs',()=>{
  const data=clone(gates);
  for(const gate of data.gates){
    gate.status='verified';gate.release_sha=productionSha;
    gate.verified_at='2026-11-21';
    gate.evidence=['https://github.com/MikeCy-com/Lefteria-FC-OS/issues/446'];
  }
  assert.equal(checkReleaseReadiness(data,productionSha,'academy-mvp').ready,true);
  for(const [date,evidence] of [
    ['2030-01-01',null],
    ['2026-02-30',null],
    [null,'staging checked'],
    [null,'http://example.com/evidence'],
    [null,'https://user:pass@github.com/secret'],
    [null,'https://example.invalid/claim'],
    [null,'https://github.com/example/proof#secret']
  ]){
    const attempt=clone(data);
    const check=attempt.gates.find(g=>g.id==='identity-delivery');
    if(date!==null)check.verified_at=date;
    if(evidence!==null)check.evidence=[evidence];
    const result=checkReleaseReadiness(attempt,productionSha,'academy-mvp');
    assert.equal(result.ready,false,JSON.stringify({date,evidence}));
    assert.ok(result.blocked.some(g=>g.id==='identity-delivery'));
  }
});
