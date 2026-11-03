import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { existsSync,readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

test('public navigation exposes real admin access, legacy redirects and safe error recovery without changing authentication realms',async()=>{
  const state='data:text/javascript,'+encodeURIComponent('export const page={status:500,error:{message:"sentinel-private-error"},url:new URL("https://fixture.test/academy")};');
  const hook=registerHooks({
    resolve(specifier,context,next){
      if(specifier==='$app/state')return {url:state,shortCircuit:true};
      if(specifier.startsWith('.')&&!/\.[a-z]+$/i.test(specifier)&&context.parentURL?.includes('/src/')){
        const path=new URL(specifier+'.ts',context.parentURL);if(existsSync(path))return next(path.href,context);
      }
      return next(specifier,context);
    },
    load(url,context,next){
      if(url.endsWith('.css'))return {format:'module',source:'export {};',shortCircuit:true};
      if(url.endsWith('.svelte')){const filename=fileURLToPath(url);return {format:'module',source:compile(readFileSync(filename,'utf8'),{filename,generate:'server'}).js.code,shortCircuit:true};}
      return next(url,context);
    }
  });
  try{
    const {default:ErrorPage}=await import('../src/routes/+error.svelte');
    const html=render(ErrorPage).body;
    assert.match(html,/Φόρτωση ξανά/);assert.match(html,/data-sveltekit-reload/);
    assert.match(html,/href="\/admin\/login"/);assert.doesNotMatch(html,/sentinel-private-error|Internal Error/);
    const {page}=await import(state);page.status=404;
    assert.match(render(ErrorPage).body,/ΔΕΝ ΒΡΗΚΑΜΕ/);
    for(const [route,location]of[['fixtures','/team?tab=results'],['calendar','/team?tab=schedule'],['venues','/team?tab=venues'],['staff','/team?tab=overview']]){
      const {load}=await import(`../src/routes/${route}/+page.server.ts`);
      assert.throws(()=>load(),error=>error.status===308&&error.location===location);
    }
    const {default:Footer}=await import('../src/lib/public/PublicFooter.svelte');
    const footer=render(Footer,{props:{club:null}}).body;
    assert.match(footer,/data-testid="footer-admin-link"/);assert.match(footer,/href="\/admin\/login"/);
    const {default:Access}=await import('../src/routes/login/+page.svelte');
    const access=render(Access,{props:{data:{site:null,branding:null,navigationGroups:[],available:false,organization:'lefteria_fc'}}}).body;
    assert.match(access,/href="\/admin\/login"/);assert.match(access,/href="\/app\/login"/);
    assert.doesNotMatch(access,/type="password"|type="email"|name="password"|sentinel/);
  }finally{hook.deregister();}
});
