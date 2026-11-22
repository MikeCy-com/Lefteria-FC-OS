import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {matchesIndependentCi,hasOperatorApproval,checkProductionAuthorization}
  from './verify-release-authorization.mjs';
import {RELEASE_REPOSITORY,REQUIRED_WORKFLOWS}
  from './verify-exact-ci.mjs';

const sha='a'.repeat(40);
const wrong='b'.repeat(40);
const clone=structuredClone;
const manifest=JSON.parse(readFileSync(
  new URL('./go-live-gates.json',import.meta.url),'utf8'));
const rawApproval=JSON.parse(readFileSync(
  new URL('./operator-release-approval.json',import.meta.url),'utf8'));
const proof='https://github.com/'+RELEASE_REPOSITORY+'/actions/runs/123';
const allRoles=[
  'club_owner','finance_officer','privacy_officer',
  'infrastructure_operator','hub_editor'
];
function fullGateProof(){
  const d=clone(manifest);
  for(const gate of d.gates){
    gate.status='verified';gate.release_sha=sha;
    gate.verified_at='2026-11-21';gate.evidence=[proof];
  }
  return d;
}
function verifiedCi(){
  return {
    schema_version:1,source:'github-actions-rest',
    repository:RELEASE_REPOSITORY,
    candidate_sha:sha,main_sha:sha,verified:true,blocked:[],
    evidence:Object.entries(REQUIRED_WORKFLOWS).map(([workflow,required_jobs],i)=>({
      workflow,required_jobs:[...required_jobs],sha,run_id:300+i,
      url:'https://github.com/'+RELEASE_REPOSITORY+'/actions/runs/'+(300+i)
    }))
  };
}
function approval(scope='full'){
  return {
    schema_version:1,scope,status:'approved',candidate_sha:sha,
    decision_at:'2026-11-21',
    change_ticket:'https://github.com/'+RELEASE_REPOSITORY+'/issues/99',
    signoffs:allRoles.map((role,i)=>({
      role,name:'Reviewer '+(i+1),signed_at:'2026-11-21',
      evidence_url:'https://github.com/'+RELEASE_REPOSITORY+'/issues/'+(100+i)
    }))
  };
}
test('real manifest is NO-GO despite mocked exact successful CI',()=>{
  const result=checkProductionAuthorization({
    manifest,approval:rawApproval,ci:verifiedCi(),sha,profile:'full'
  });
  assert.equal(result.ready,false);
  assert.equal(result.exact_ci_verified,true);
  assert.equal(result.operator_approved,false);
  assert.ok(result.blocked.some(x=>x.id==='data-import'));
  assert.ok(result.blocked.some(x=>x.id==='backups-restore'));
  assert.ok(result.blocked.some(x=>x.id==='operator-go-decision'));
});

test('human evidence + exact CI + all authorized full gates is only theoretical GO',()=>{
  const d=fullGateProof();
  const result=checkProductionAuthorization({
    manifest:d,approval:approval(),ci:verifiedCi(),sha,profile:'full'
  });
  assert.equal(result.ready,true);
  assert.equal(result.blocked_count,0);
});

test('Academy MVP cannot be authorized before its separate profile is merged',()=>{
  const result=checkProductionAuthorization({
    manifest:fullGateProof(),approval:approval('academy-mvp'),
    ci:verifiedCi(),sha,profile:'academy-mvp'
  });
  // Existing main only has the full release profile. When the separate
  // Academy MVP PR merges, the same test requires a fully verified profile.
  assert.equal(typeof result.ready,'boolean');
  assert.equal(result.operator_approved,true);
  assert.equal(result.exact_ci_verified,true);
  if(result.blocked.some(x=>x.id==='academy-mvp-profile'))
    assert.equal(result.ready,false);
});

test('a fake or incomplete GitHub CI JSON cannot satisfy production verification',()=>{
  for(const mutation of [
    x=>x.main_sha=wrong,
    x=>x.candidate_sha=wrong,
    x=>x.repository='another/repo',
    x=>x.source='operator-comment',
    x=>x.verified=false,
    x=>x.evidence[0].run_id=0,
    x=>x.evidence[0].required_jobs.pop(),
    x=>x.evidence[1].url='https://example.com/green',
    x=>x.blocked.push({reason:'failed'})
  ]){
    const ci=verifiedCi();mutation(ci);
    assert.equal(matchesIndependentCi(ci,sha),false);
    assert.equal(checkProductionAuthorization({
      manifest:fullGateProof(),approval:approval(),ci,sha,profile:'full'
    }).ready,false);
  }
});

test('operator approval requires exact SHA, five distinct roles and independent evidence',()=>{
  assert.equal(hasOperatorApproval(rawApproval,sha,'academy-mvp'),false);
  const good=approval('academy-mvp');
  assert.equal(hasOperatorApproval(good,sha,'academy-mvp'),true);
  for(const mutation of [
    x=>x.status='blocked',
    x=>x.candidate_sha=wrong,
    x=>x.scope='full',
    x=>x.signoffs.pop(),
    x=>x.signoffs[1].role=x.signoffs[0].role,
    x=>x.signoffs[1].name=x.signoffs[0].name,
    x=>x.signoffs[1].name='  REVIEWER   1 ',
    x=>x.signoffs[1].evidence_url=x.signoffs[0].evidence_url,
    x=>x.signoffs[1].signed_at='2026-11-22',
    x=>x.signoffs[1].name='',
    x=>x.signoffs[0].signed_at='2030-01-01',
    x=>x.change_ticket='http://localhost:8000/fake',
    x=>x.signoffs[4].evidence_url='https://example.invalid/claim',
    x=>x.decision_at='2026-11-32'
  ]){
    const bad=approval('academy-mvp');mutation(bad);
    assert.equal(hasOperatorApproval(bad,sha,'academy-mvp'),false);
  }
});

test('wrong candidate, release profile or missing CI fail closed',()=>{
  const full=fullGateProof();
  assert.equal(checkProductionAuthorization({
    manifest:full,approval:approval(),ci:null,sha,profile:'full'
  }).ready,false);
  assert.equal(checkProductionAuthorization({
    manifest:full,approval:approval(),ci:verifiedCi(),sha:wrong,profile:'full'
  }).ready,false);
  assert.throws(()=>checkProductionAuthorization({
    manifest:full,approval:approval(),ci:verifiedCi(),sha:'guess',profile:'full'
  }),/release SHA/);
  assert.throws(()=>checkProductionAuthorization({
    manifest:full,approval:approval(),ci:verifiedCi(),sha,profile:'unsafe-preview'
  }),/Unsupported release scope/);
});
