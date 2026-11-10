#!/usr/bin/env node
/**
 * Source-led UI/API inventory. Runs offline against the OS repository.
 * The original Lefteria-FC baseline is pinned in the audit manifest; this
 * inventory NEVER calls live production, grants permissions or asserts that
 * a source file proves its workflow works.
 *
 * Usage: node frontend/scripts/audit-original-parity.mjs
 *        node frontend/scripts/audit-original-parity.mjs --json
 */
import {readdirSync,readFileSync,statSync} from 'node:fs';
import {join,relative,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';

const root=resolve(fileURLToPath(new URL('../..',import.meta.url)));
function filesAt(dir,extensions){
  const base=join(root,dir);const files=[];
  function walk(folder){
    for(const d of readdirSync(folder,{withFileTypes:true})){
      const file=join(folder,d.name);
      if(d.isDirectory())walk(file);
      else if(d.isFile()&&extensions.some(ext=>file.endsWith(ext)))
        files.push(relative(root,file).replaceAll('\\','/'));
    }
  }
  walk(base);return files.sort();
}
const read=path=>readFileSync(join(root,path),'utf8');
const allPages=filesAt('frontend/src/routes',['+page.svelte']);
const loadFiles=filesAt('frontend/src/routes',['+page.server.ts','+layout.server.ts','+server.ts']);
const uiFiles=filesAt('frontend/src',['.svelte','.ts','.js']).filter(x=>!x.includes('/tests/'));
const apiFiles=filesAt('backend/src/LefteriaFc.Api',['Endpoints.cs','Program.cs']);
const backendFiles=filesAt('backend/src/LefteriaFc.Api',['.cs']);
const route=path=>path.slice('frontend/src/routes/'.length)
  .replace(/\/(?:\+page|\+layout|\+server)(?:\.server)?\.(?:svelte|ts)$/,'')
  .replace(/^\+page\.svelte$/,'').replace(/\/(?:\([^/]+\))(?=\/|$)/g,'')
  .replace(/\[\[?([^\]/]+)\]\]?/g,':$1');
const frontendRoutes=[...new Set(allPages.map(x=>'/'+route(x).replace(/^\/+/,'')))].sort();
const patterns=/\bMap(Get|Post|Put|Patch|Delete)\s*\(\s*["']([^"']+)["']/g;
const apiVerbs=[];
for(const path of apiFiles){
  for(const m of read(path).matchAll(patterns))
    apiVerbs.push({method:m[1].toUpperCase(),pattern:m[2],file:path});
}
const hardcodedCalls=[];
const requestPattern=/\b(?:fetch|apiFetch|request)\s*\(\s*["'`]((?:\/api\/)[^"'`?#]*)/g;
for(const path of uiFiles){
  for(const m of read(path).matchAll(requestPattern))
    hardcodedCalls.push({path:m[1],source:path});
}
const placeholders=['/join','/trials','/academy/registration','/vote'];
const majorPublic=['/','/team','/academy','/academy/groups','/news',
  '/events','/shop','/contact','/about','/partners','/sponsors/first-team'];
const requiredRoles=['parent','player','coach','management','fan'];
const mobileNav=read('frontend/src/lib/mobile/navigation.ts');
const publicNav=read('frontend/src/lib/public/navigation.ts');
const knownLegacy=Object.freeze({
  source:'MikeCy-com/Lefteria-FC',
  baselineSha:'91eafcc9efd01ad177b4f13a869cd5af7041e5b2',
  publicSource:'frontend/src/club-site/navigation.js',
  appRoleSource:'frontend/src/mobile/pages',
  adminSource:'frontend/src/pages/AdminPanel.jsx',
  apiSource:'backend/server.py',
  knownPublicSections:['home','club','team','academy','matches','news','events','shop','contact']
});
const summary={
  schema_version:1,
  comparison_to:knownLegacy,
  os:{pages:frontendRoutes.length,loaders:loadFiles.length,
    backend_api_files:apiFiles.length,api_http_verbs:apiVerbs.length,
    ui_source_files:uiFiles.length,static_client_api_calls:hardcodedCalls.length},
  expected_source_public_sections:knownLegacy.knownPublicSections.map(id=>
    ({id,present:new RegExp('id: [\\x27\\x22]'+id+'[\\x27\\x22]').test(publicNav)})),
  expected_mobile_roles:requiredRoles.map(role=>
    ({role,present:new RegExp('^\\s*'+role+':\\s*\\{','m').test(mobileNav)})),
  sampled_public_routes:majorPublic.map(path=>({path,present:frontendRoutes.includes(path)})),
  unported_sensitive_workflows:placeholders.map(path=>{
    const file=path==='/'?'frontend/src/routes/+page.svelte':
      'frontend/src/routes'+path+'/+page.svelte';
    const content=allPages.includes(file)?read(file):'';
    return {path,page_exists:!!content,
      safe_placeholder:/(?:δεν έχουν|δεν έχει|δεν είναι|δεν έχει ακόμη|δεν έχουν ακόμη|δεν συλλέγουμε|δεν αποθηκεύουμε)/i.test(content),
      operationally_verified:false};
  }),
  routes:frontendRoutes,
  api_endpoints:apiVerbs,
  hardcoded_client_api_calls:hardcodedCalls
};
const failures=[
  ...summary.expected_source_public_sections.filter(x=>!x.present).map(x=>'missing_menu:'+x.id),
  ...summary.expected_mobile_roles.filter(x=>!x.present).map(x=>'missing_role:'+x.role),
  ...summary.sampled_public_routes.filter(x=>!x.present).map(x=>'missing_page:'+x.path)
];
if(process.argv.includes('--json'))console.log(JSON.stringify({summary,failures},null,2));
else{
  console.log('Lefteria FC original → OS static inventory (NOT end-to-end verification)');
  console.log('Pages:',summary.os.pages,'loaders:',summary.os.loaders,
    'API endpoint declarations:',summary.os.api_http_verbs,
    'UI files:',summary.os.ui_source_files);
  console.log('Known source public categories:',summary.expected_source_public_sections.filter(x=>x.present).length+'/9');
  console.log('Mobile roles:',summary.expected_mobile_roles.filter(x=>x.present).length+'/5');
  console.log('Sensitive source workflows:',JSON.stringify(summary.unported_sensitive_workflows));
  if(failures.length)console.error('Structural discrepancies:',failures.join(', '));
  console.log('These counts do not prove runtime, authorization, visual, or payment parity.');
}
if(failures.length)process.exitCode=1;
