import test from 'node:test';
import assert from 'node:assert/strict';
import {verifyPreviewRuntimeImages,PROJECT,REPOSITORY}
  from './verify-preview-runtime-images.mjs';

const sha='a'.repeat(40);
const source='https://github.com/'+REPOSITORY;
const now=new Date('2026-11-22T18:20:00Z');
const image=name=>({
  reference:'lefteria-fc-os-'+name+':'+sha,
  id:'sha256:'+({api:'1',web:'2',import:'3'}[name]).repeat(64),
  archive:'lefteria-fc-os-'+name+'.tar.gz'
});
const manifest=()=>({
  schema_version:1,repository:REPOSITORY,source_sha:sha,
  platform:'linux/amd64',images:{api:image('api'),web:image('web'),import:image('import')}
});
const row=name=>({
  service:name,project:PROJECT,
  image_id:image(name).id,image_ref:image(name).id,
  revision:sha,source,running:true,healthy:true,read_only:true
});
const observations=()=>({
  schema_version:1,observed_at_utc:'2026-11-22T18:19:00Z',
  containers:[row('api'),row('web')]
});
const verify=(m=manifest(),o=observations())=>
  verifyPreviewRuntimeImages({manifest:m,observations:o,sha,now});
test('passes exact candidate API/web images with healthy read-only preview metadata',()=>{
  const result=verify();
  assert.equal(result.verified,true);
  assert.deepEqual(result.checked_services,['api','web']);
  assert.deepEqual(result.blocked,[]);
  assert.match(result.limitation,/not independently authenticated/);
});
test('also accepts the exact verified tag but refuses mutable latest',()=>{
  const o=observations();
  o.containers[0].image_ref=image('api').reference;
  assert.equal(verify(manifest(),o).verified,true);
  o.containers[0].image_ref='lefteria-fc-os-api:latest';
  assert.equal(verify(manifest(),o).verified,false);
});
test('refuses mismatched image ID, source SHA or OCI source and wrong project',()=>{
  for(const mutate of [
    o=>o.containers[0].image_id='sha256:'+'9'.repeat(64),
    o=>o.containers[0].image_ref='lefteria-fc-os-api:latest',
    o=>o.containers[0].revision='b'.repeat(40),
    o=>o.containers[0].source='https://github.com/another/repo',
    o=>o.containers[0].project='lefteria-live'
  ]){
    const o=observations();mutate(o);
    assert.equal(verify(manifest(),o).verified,false);
  }
});
test('refuses unhealthy, stopped and writable-root containers',()=>{
  for(const mutate of [
    o=>o.containers[1].running=false,
    o=>o.containers[1].healthy=false,
    o=>o.containers[1].read_only=false
  ]){
    const o=observations();mutate(o);
    assert.ok(verify(manifest(),o).blocked.some(x=>x.includes('runtime_')));
  }
});
test('refuses duplicate, extra, missing and unapproved potentially private observation fields',()=>{
  for(const mutate of [
    o=>o.containers=[row('api'),row('api')],
    o=>o.containers=[row('api')],
    o=>o.containers.push(row('web')),
    o=>o.containers[0].secret_env='DATABASE_PASSWORD=do-not-echo'
  ]){
    const o=observations();mutate(o);
    const result=verify(manifest(),o);
    assert.equal(result.verified,false);
    assert.ok(!JSON.stringify(result).includes('do-not-echo'));
  }
});
test('rejects stale, impossible and future observation time',()=>{
  for(const date of [
    '2026-11-22T17:40:00Z',
    '2026-11-22T18:20:01Z',
    '2026-02-30T18:19:00Z',
    'not-an-utc-date',
    'databasepassword-sneaky-value'
  ]){
    const o=observations();o.observed_at_utc=date;
    const result=verify(manifest(),o);
    assert.equal(result.verified,false);
    if(!date.startsWith('2026-'))
      assert.equal(result.observed_at_utc,null);
    assert.ok(!JSON.stringify(result).includes('databasepassword-sneaky-value'));
  }
});
test('rejects invalid release manifests, malformed SHA and absent evidence',()=>{
  for(const mutate of [
    m=>m.platform='linux/arm64',
    m=>m.repository='another/repo',
    m=>m.images.import.archive='bad.tgz',
    m=>m.source_sha='b'.repeat(40),
    m=>m.images.web.id='bad'
  ]){
    const m=manifest();mutate(m);
    assert.equal(verify(m,observations()).verified,false);
  }
  assert.equal(verify(manifest(),null).verified,false);
  assert.throws(()=>verifyPreviewRuntimeImages({
    manifest:manifest(),observations:observations(),sha:'short',now
  }),/SHA/);
});
