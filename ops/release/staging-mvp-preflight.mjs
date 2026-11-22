#!/usr/bin/env node
/**
 * Read-only Academy MVP staging preflight.
 * Checks health, public/admin login and denial of unauthenticated private data.
 * Never deploys, migrates, reads private data or calls payment endpoints.
 *
 * Required: --origin=https://<isolated-preview-host>[:8443]
 * Optional: LEFTERIA_PREVIEW_GATE_AUTHORIZATION (e.g. gate Basic header).
 *            The header is sent ONLY to the exact pre-approved origin.
 * This is an HTTP readiness signal, not release SHA, backup or payment proof.
 */
import process from 'node:process';
import {fileURLToPath} from 'node:url';
import {readBoundedResponse} from './read-bounded-response.mjs';

export const STAGING_CHECKS=Object.freeze([
  {path:'/api/health/live',status:200,kind:'json',bodyStatus:'live'},
  {path:'/api/health/ready',status:200,kind:'json',bodyStatus:'ready'},
  {path:'/',status:200,kind:'html'},
  {path:'/admin/login',status:200,kind:'html'},
  // MVP-disabled features must be proven blocked by the backend itself,
  // not merely hidden behind an anonymous 401 or a missing provider key.
  {path:'/api/admin/pos/sites?organization_id=lefteria_fc_academy',
    status:503,kind:'disabled',bodyCode:'mvp_feature_disabled'},
  {path:'/api/admin/finance/payabl/payment-requests?organization_id=lefteria_fc_academy',
    status:503,kind:'disabled',bodyCode:'mvp_feature_disabled'},
  {path:'/api/mobile/hub/fan-subscription/plans',
    status:503,kind:'disabled',bodyCode:'mvp_feature_disabled'},
  // Sensitive Academy enrollment and roster reads are protected at the API,
  // not only through the administrator UI.
  {path:'/api/admin/academy/intake',status:[401,403],kind:'deny'},
  {path:'/api/admin/football/players?organization_id=lefteria_fc_academy',
    status:[401,403],kind:'deny'},
  {path:'/api/mobile/family',status:[401,403],kind:'deny'},
  {path:'/api/mobile/players-list',status:[401,403],kind:'deny'}
]);

export function requireIsolatedStagingOrigin(raw) {
  if(typeof raw!=='string'||!raw)throw new Error('An explicit isolated staging HTTPS origin is required.');
  let url;
  try{url=new URL(raw);}catch{throw new Error('Invalid staging origin.');}
  const host=url.hostname.toLowerCase();
  const previewHost=host.endsWith('.hstgr.cloud')&&host!=='hstgr.cloud';
  const stagingDomain=/^(staging|preview|os-preview)([.-][a-z0-9-]+)*\.lefteriafc\.cy$/.test(host);
  if(url.protocol!=='https:' || url.username || url.password ||
    url.pathname!=='/' || url.search || url.hash ||
    !['','443','8443'].includes(url.port) ||
    (!previewHost&&!stagingDomain) ||
    host==='lefteriafc.cy'||host==='www.lefteriafc.cy')
    throw new Error('Refusing any non-isolated, non-HTTPS or live-production origin.');
  return url.origin;
}

export async function checkStagingMvp({
  origin, fetcher=fetch, gateAuthorization='', checks=STAGING_CHECKS
}) {
  const base=requireIsolatedStagingOrigin(origin);
  if(typeof gateAuthorization!=='string' ||
     /[\r\n]/.test(gateAuthorization) || gateAuthorization.length>2048)
    throw new Error('Invalid preview gate authorization value.');
  const evidence=[];
  for(const check of checks){
    const url=new URL(check.path,base);
    if(url.origin!==base)throw new Error('Cross-origin preflight request refused.');
    let response;
    try {
      const headers={'Cache-Control':'no-store','Accept':check.kind==='html'
        ?'text/html':'application/json'};
      if(gateAuthorization)headers.Authorization=gateAuthorization;
      response=await fetcher(url.toString(),{
        method:'GET',headers,redirect:'manual',credentials:'omit',
        signal:AbortSignal.timeout(8000)
      });
    }catch(err){
      evidence.push({path:check.path,ok:false,reason:'request_failed'});
      continue;
    }
    const expected=Array.isArray(check.status)?check.status:[check.status];
    let ok=expected.includes(response.status);
    if(ok&&(check.kind==='json'||check.kind==='disabled')){
      try{
        const type=(response.headers?.get('content-type')??'').toLowerCase();
        if(!type.includes('application/json'))ok=false;
        else{
          const body=JSON.parse(await readBoundedResponse(response,8192));
          ok=check.kind==='json'?body?.status===check.bodyStatus:
            body?.code===check.bodyCode;
        }
      }catch{ok=false;}
    }else if(ok&&check.kind==='html'){
      // A chunked or lying-content-length 200 may contain an enormous body.
      // Stream at most 1 MiB and NEVER log any response bytes.
      try{
        const type=(response.headers?.get('content-type')??'').toLowerCase();
        if(!type.includes('text/html'))ok=false;
        else{
          const html=await readBoundedResponse(response,1024*1024);
          ok=html.length>0&&
            /<(?:!doctype\s+html|html\b)/i.test(html);
        }
      }catch{ok=false;}
    }
    evidence.push({path:check.path,ok,status:response.status,
      reason:ok?'matched':response.status>=300&&response.status<400
        ?'redirect_refused':'unexpected_http_or_body'});
  }
  return {ready:evidence.length===checks.length&&evidence.every(x=>x.ok),
    checks:evidence.length,passed:evidence.filter(x=>x.ok).length,
    origin:base,evidence,
    warnings:['No deployment performed','Does not prove deployed commit SHA',
      'Does not verify real OTP, backup/restore, private role workflows or financial reconciliation',
      'Optional payment, POS and fan routes must remain deliberately disabled for this MVP profile']};
}

function main(){
  const args=process.argv.slice(2);
  const origins=args.filter(x=>x.startsWith('--origin='));
  if(args.length!==1||origins.length!==1){
    console.error('Usage: node ops/release/staging-mvp-preflight.mjs --origin=https://isolated-staging-host[:8443]');
    process.exitCode=2;return;
  }
  checkStagingMvp({origin:origins[0].slice('--origin='.length),
    gateAuthorization:process.env.LEFTERIA_PREVIEW_GATE_AUTHORIZATION??''})
    .then(report=>{
      console.log(JSON.stringify(report,null,2));
      if(!report.ready)process.exitCode=1;
    }).catch(err=>{
      // Never print supplied Authorization or sensitive HTTP bodies.
      console.error('Staging preflight refused:',err.message);
      process.exitCode=2;
    });
}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1])main();
