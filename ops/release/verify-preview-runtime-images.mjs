#!/usr/bin/env node
/**
 * Offline, read-only preview runtime image attestation.
 *
 * Cross-check a pinned preview manifest against MINIMAL, operator-captured
 * Docker metadata. No Docker/hosting calls, credentials, writes or deployment.
 * G0 independent infrastructure inventory is a separate mandatory gate.
 */
import {readFileSync,statSync} from 'node:fs';
import process from 'node:process';
import {fileURLToPath} from 'node:url';

export const PROJECT='lefteria-os-preview';
export const REPOSITORY='MikeCy-com/Lefteria-FC-OS';
const SOURCE='https://github.com/'+REPOSITORY;
const SHA=/^[0-9a-f]{40}$/;
const IMAGE_ID=/^sha256:[0-9a-f]{64}$/;
const SERVICES=Object.freeze(['api','web']);
const ALLOWED_FIELDS=new Set([
  'service','project','image_id','image_ref','revision','source',
  'running','healthy','read_only'
]);
const WINDOW_MS=30*60*1000;

export function verifyPreviewRuntimeImages({
  manifest,observations,sha,now=new Date()
}) {
  if(typeof sha!=='string'||!SHA.test(sha))
    throw new Error('Exact lowercase 40-character source SHA required.');
  if(!(now instanceof Date)||Number.isNaN(now.getTime()))
    throw new Error('Valid comparison time required.');
  const blocked=[];
  const add=reason=>{if(!blocked.includes(reason))blocked.push(reason);};
  if(!manifest||typeof manifest!=='object'||Array.isArray(manifest)||
     manifest.schema_version!==1||manifest.repository!==REPOSITORY||
     manifest.source_sha!==sha||manifest.platform!=='linux/amd64')
    add('candidate_manifest_invalid');
  if(!observations||typeof observations!=='object'||
     Array.isArray(observations)||observations.schema_version!==1)
    add('runtime_observations_invalid');
  const iso=observations?.observed_at_utc;
  const validTimestamp=typeof iso==='string'&&
    /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,3})?Z$/.test(iso)&&
    Number.isFinite(Date.parse(iso))&&
    new Date(iso).toISOString().slice(0,19)===iso.slice(0,19);
  if(!validTimestamp||now.getTime()-Date.parse(iso)<0||
     now.getTime()-Date.parse(iso)>WINDOW_MS)
    add('observation_stale_or_invalid');
  const images=manifest?.images;
  if(!images||typeof images!=='object'||Array.isArray(images)||
     !['api','web','import'].every(name=>
       images[name]&&typeof images[name]==='object'&&
       !Array.isArray(images[name])&&
       images[name].reference==='lefteria-fc-os-'+name+':'+sha&&
       IMAGE_ID.test(images[name].id)&&
       images[name].archive==='lefteria-fc-os-'+name+'.tar.gz'))
    add('candidate_images_invalid');
  const observed=observations?.containers;
  if(!Array.isArray(observed)||observed.length!==SERVICES.length){
    add('expected_two_runtime_services');
  }else{
    const seen=new Set();
    for(const row of observed){
      if(!row||typeof row!=='object'||Array.isArray(row)||
         Object.keys(row).some(k=>!ALLOWED_FIELDS.has(k))){
        add('unapproved_observation_fields');continue;
      }
      const service=row.service;
      if(!SERVICES.includes(service)||seen.has(service)){
        add('runtime_service_set_invalid');continue;
      }
      seen.add(service);
      if(row.project!==PROJECT)add('runtime_compose_project_mismatch');
      if(row.running!==true||row.healthy!==true)
        add('runtime_container_not_healthy');
      if(row.read_only!==true)add('runtime_root_filesystem_not_read_only');
      if(row.revision!==sha||row.source!==SOURCE)
        add('runtime_oci_revision_mismatch');
      // Verified preview bootstrap intentionally pins Compose images by
      // immutable sha256 image ID, not mutable tags. Older image-ref inputs
      // may use the exact verified tag, but never latest or an unrelated tag.
      if(!images?.[service]||
         row.image_id!==images[service].id||
         (row.image_ref!==images[service].id&&
          row.image_ref!==images[service].reference))
        add('running_image_not_candidate_bundle');
    }
    if(seen.size!==SERVICES.length)add('runtime_service_set_invalid');
  }
  return {
    schema_version:1,repository:REPOSITORY,candidate_sha:sha,
    observed_at_utc:validTimestamp?iso:null,
    verified:blocked.length===0,checked_services:[...SERVICES],blocked,
    limitation:'Operator-supplied sanitized Docker metadata is not independently authenticated. Still requires G0 identity/isolation, signed evidence, DB/media restore, real five-role staging tests and rollback; not release approval.'
  };
}

function readBoundedJson(path){
  if(typeof path!=='string'||!path||path.length>1024)
    throw new Error('Explicit local evidence file required.');
  const stat=statSync(path);
  if(!stat.isFile()||stat.size<=0||stat.size>128*1024)
    throw new Error('Evidence size/type refused.');
  return JSON.parse(readFileSync(path,'utf8'));
}
async function main(args=process.argv.slice(2)){
  const opts=new Map();
  for(const arg of args){
    const match=/^--(sha|manifest|observations)=(.+)$/.exec(arg);
    if(!match||opts.has(match[1])){
      console.error('Usage: verify-preview-runtime-images.mjs --sha=<40hex> --manifest=<local-file> --observations=<sanitized-file>');
      process.exitCode=2;return;
    }
    opts.set(match[1],match[2]);
  }
  if(opts.size!==3){
    console.error('Required: SHA, preview bundle manifest, sanitized preview observations.');
    process.exitCode=2;return;
  }
  try{
    const result=verifyPreviewRuntimeImages({
      sha:opts.get('sha'),
      manifest:readBoundedJson(opts.get('manifest')),
      observations:readBoundedJson(opts.get('observations'))
    });
    console.log(JSON.stringify(result,null,2));
    if(!result.verified)process.exitCode=1;
  }catch{
    // No file contents, paths or parser exception details logged.
    console.error('Preview image attestation blocked: invalid or unavailable inputs.');
    process.exitCode=1;
  }
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1])
  await main();
