import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync,readFileSync} from 'node:fs';
import {registerHooks} from 'node:module';
import {fileURLToPath} from 'node:url';
import {compile} from 'svelte/compiler';
import {render} from 'svelte/server';

// Reuse the same actual Svelte server-component rendering approach as the
// established Academy integration tests; no mock markup or live API access.
registerHooks({
  resolve(specifier,context,nextResolve){
    if(specifier==='$app/state')
      return {url:'data:text/javascript,'+encodeURIComponent(
        'export const page={url:new URL("https://fixture.test/pages/development-guide"),data:{websitePages:{lefteria_fc:[],lefteria_fc_academy:[]}}};'
      ),shortCircuit:true};
    if(specifier.startsWith('.')&&/\/src\//.test(context.parentURL??'')&&!/\.[a-z]+$/i.test(specifier)){
      const candidate=new URL(specifier+'.ts',context.parentURL);
      if(existsSync(candidate))return nextResolve(candidate.href,context);
    }
    return nextResolve(specifier,context);
  },
  load(url,context,nextLoad){
    if(url.endsWith('.css'))return {format:'module',source:'export {};',shortCircuit:true};
    if(url.endsWith('.svelte')){
      const filename=fileURLToPath(url);
      return {format:'module',source:compile(readFileSync(filename,'utf8'),
        {filename,generate:'server'}).js.code,shortCircuit:true};
    }
    return nextLoad(url,context);
  }
});

const slug='development-guide';
const content=(organization_id,menu_parent,text)=>({
  organization_id,slug,published_at:'2026-11-15T12:00:00Z',
  page:{title:'Πλάνο Ανάπτυξης',summary:'Επίσημη δημοσιευμένη παρουσίαση',
    seo_title:'Πλάνο Ανάπτυξης · SEO',seo_description:'Δημοσιευμένο κείμενο',
    menu_parent,menu_order:0,
    blocks:[{type:'heading',text:'Μαθαίνουμε κάθε μέρα',url:null},
      {type:'paragraph',text,url:null}]}
});

test('real Svelte SSR published CMS page is accessible, escaped and returns to Academy',async()=>{
  const {default:Page}=await import('../src/routes/pages/[slug]/+page.svelte');
  const academy=content('lefteria_fc_academy','academy','ACADEMY_PRIVATE_ORG_PUBLIC_TEXT <script>alert(1)</script>');
  const html=render(Page,{props:{data:{
    site:null,branding:null,organization:'lefteria_fc_academy',
    navigationGroups:[],available:true,published:academy
  }}}).body;
  assert.match(html,/data-testid="website-published-page"/);
  assert.match(html,/data-organization="lefteria_fc_academy"/);
  assert.match(html,/data-testid="website-page-breadcrumbs"/);
  assert.match(html,/aria-current="page"/);
  assert.match(html,/href="\/academy"[^>]*data-testid="website-page-parent-link"/);
  assert.match(html,/href="\/academy"[^>]*data-testid="website-page-return"/);
  assert.match(html,/Πλάνο Ανάπτυξης/);
  assert.match(html,/Μαθαίνουμε κάθε μέρα/);
  assert.match(html,/ACADEMY_PRIVATE_ORG_PUBLIC_TEXT/);
  assert.match(html,/&lt;script>/);
  assert.doesNotMatch(html,/<script>alert\(1\)<\/script>/);
  assert.doesNotMatch(html,/FIRST_TEAM_ONLY_PUBLIC_TEXT/);
});

test('real Svelte SSR First Team CMS page preserves published content and correctly scoped return',async()=>{
  const {default:Page}=await import('../src/routes/pages/[slug]/+page.svelte');
  const team=content('lefteria_fc','news','FIRST_TEAM_ONLY_PUBLIC_TEXT');
  const html=render(Page,{props:{data:{
    site:null,branding:null,organization:'lefteria_fc',
    navigationGroups:[],available:true,published:team
  }}}).body;
  assert.match(html,/href="\/news\?organization_id=lefteria_fc"[^>]*data-testid="website-page-parent-link"/);
  assert.match(html,/href="\/news\?organization_id=lefteria_fc"[^>]*data-testid="website-page-return"/);
  assert.match(html,/FIRST_TEAM_ONLY_PUBLIC_TEXT/);
  assert.doesNotMatch(html,/ACADEMY_PRIVATE_ORG_PUBLIC_TEXT/);
  assert.match(html,/data-testid="website-published-content"/);
});
