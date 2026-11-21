import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {checkReleaseReadiness} from './check-go-live.mjs';
import {RELEASE_REPOSITORY,REQUIRED_WORKFLOWS} from './verify-exact-ci.mjs';

const sha='a'.repeat(40);
const other='b'.repeat(40);
const gates=JSON.parse(readFileSync(new URL('./go-live-gates.json',import.meta.url),'utf8'));
const clone=value=>structuredClone(value);
const proofUrl='https://github.com/'+RELEASE_REPOSITORY+'/actions/runs/123';
function verifiedCi(){
  return {
    schema_version:1,source:'github-actions-rest',repository:RELEASE_REPOSITORY,
    candidate_sha:sha,main_sha:sha,verified:true,blocked:[],
    evidence:Object.entries(REQUIRED_WORKFLOWS).map(([name,jobs],index)=>({
      workflow:name,run_id:123+index,sha,
      url:'https://github.com/'+RELEASE_REPOSITORY+'/actions/runs/'+(123+index),
      required_jobs:[...jobs]
    }))
  };
}
function approvedCopy(){
  const x=clone(gates);
  for(const gate of x.gates){
    gate.status='verified';gate.release_sha=sha;
    gate.verified_at='2026-11-21';
    gate.evidence=[proofUrl];
  }
  x.release_decision={
    status:'approved',scope:'full-production',
    release_sha:sha,approved_at:'2026-11-21',
    approved_by:'Human Release Officer',
    ticket_url:'https://github.com/'+RELEASE_REPOSITORY+'/issues/123'
  };
  return x;
}
test('real release snapshot stays blocked, even if code CI passes',()=>{
  const report=checkReleaseReadiness(gates,sha,verifiedCi());
  assert.equal(report.ready,false);
  assert.equal(report.exact_ci_verified,true);
  assert.equal(report.blocked_count,gates.gates.length+1);
  for(const mandatory of [
    'api-ci','data-import','finance-reconciliation','backups-restore',
    'privacy-security','production-identity','hub-education','cutover-rollback'
  ])assert.ok(report.blocked.some(x=>x.id===mandatory),mandatory);
});

test('a manifest with fabricated green links alone cannot authorize go-live',()=>{
  const data=approvedCopy();
  let result=checkReleaseReadiness(data,sha);
  assert.equal(result.ready,false);
  assert.ok(result.blocked.some(x=>x.id==='independent-ci'));
  result=checkReleaseReadiness(data,other,verifiedCi());
  assert.equal(result.ready,false);
  assert.ok(result.blocked.some(x=>x.id==='independent-ci'));
  result=checkReleaseReadiness(data);
  assert.equal(result.ready,false);
  assert.ok(result.blocked.some(x=>x.id==='candidate-release-sha'));
});

test('only an explicitly signed-off manifest plus independently verified exact CI can satisfy schema',()=>{
  const data=approvedCopy();
  const result=checkReleaseReadiness(data,sha,verifiedCi());
  assert.equal(result.ready,true);
  assert.equal(result.blocked_count,0);
  assert.equal(result.exact_ci_verified,true);
  const noApproval=clone(data);
  noApproval.release_decision.status='blocked';
  const denied=checkReleaseReadiness(noApproval,sha,verifiedCi());
  assert.equal(denied.ready,false);
  assert.ok(denied.blocked.some(x=>x.id==='operator-release-decision'));
});

test('independent CI evidence requires six named jobs and correct source, main SHA and links',()=>{
  const data=approvedCopy();
  for(const mutate of [
    ci=>ci.source='operator-claim',
    ci=>ci.main_sha=other,
    ci=>ci.repository='another/club',
    ci=>ci.verified=false,
    ci=>ci.evidence[0].required_jobs.pop(),
    ci=>ci.evidence[0].url='https://evil.example.org/actions/runs/123',
    ci=>ci.blocked.push({gate:'any',reason:'not verified'})
  ]){
    const ci=verifiedCi();
    mutate(ci);
    const result=checkReleaseReadiness(data,sha,ci);
    assert.equal(result.ready,false);
    assert.ok(result.blocked.some(x=>x.id==='independent-ci'));
  }
});

test('cannot delete mandatory production, identity, hub or backup gate',()=>{
  for(const omitted of [
    'api-ci','data-import','privacy-security','finance-reconciliation',
    'cutover-rollback','backups-restore','role-mobile-parity','production-identity','hub-education'
  ]){
    const data=clone(gates);
    data.gates=data.gates.filter(x=>x.id!==omitted);
    assert.throws(()=>checkReleaseReadiness(data,sha),/Mandatory/);
  }
});

test('invalid evidence URL, duplicate gate, forged status and broken dependencies fail closed',()=>{
  const badUrl=approvedCopy();
  badUrl.gates[0].evidence=['http://localhost:3000/green'];
  assert.throws(()=>checkReleaseReadiness(badUrl,sha),/Invalid or duplicate/);
  const duplicate=clone(gates);duplicate.gates.push(clone(duplicate.gates[0]));
  assert.throws(()=>checkReleaseReadiness(duplicate,sha),/duplicate/);
  const unknown=clone(gates);unknown.gates[0].status='looks_live';
  assert.throws(()=>checkReleaseReadiness(unknown,sha),/Invalid or duplicate/);
  const badDependency=clone(gates);
  badDependency.gates.find(x=>x.id==='hub-education').requires.push('nonexistent-gate');
  assert.throws(()=>checkReleaseReadiness(badDependency,sha),/Unknown release gate dependency/);
  const selfDependency=clone(gates);
  selfDependency.gates.find(x=>x.id==='hub-education').requires=['hub-education'];
  assert.throws(()=>checkReleaseReadiness(selfDependency,sha),/dependency/);
  assert.throws(()=>checkReleaseReadiness(gates,'not-a-sha'),/commit SHA/);
});

test('dependent verified gate remains blocked when the prerequisite is not verified',()=>{
  const data=approvedCopy();
  data.gates.find(x=>x.id==='privacy-security').status='blocked';
  const result=checkReleaseReadiness(data,sha,verifiedCi());
  assert.equal(result.ready,false);
  assert.ok(result.blocked.some(x=>x.reason==='required_gate_not_verified:privacy-security'));
});
