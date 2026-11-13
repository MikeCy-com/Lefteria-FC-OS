import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {searchPublishedHubEducation} from '../src/lib/mobile/published-education.ts';

const source=readFileSync(new URL('../src/lib/mobile/components/MobilePublishedHubEducation.svelte',import.meta.url),'utf8');
const article=(id,audience='parent')=>({id,organization_id:'lefteria_fc_academy',title:'Τίτλος',body:'Ασφαλές άρθρο',audience,age_min:null,age_max:null,published_at:'2026-11-12T10:00:00Z'});
test('published learning progress affects only server-authorized loaded items',()=>{
  const allowed=[article('a'),article('b')];
  const results=searchPublishedHubEducation(allowed,'');
  assert.deepEqual(results,allowed);
  assert.match(source,/const completedCount=\$derived\(items\.filter\(item=>completedIds\.includes\(item\.id\)\)\.length\)/);
  assert.match(source,/aria-pressed=\{completedIds\.includes\(item\.id\)\}/);
  assert.match(source,/completedIds=completedIds\.includes\(item\.id\)/);
  assert.doesNotMatch(source,/localStorage|sessionStorage|fetch\(/);
});
test('refresh and unmount clear completed markers before another identity or organization is displayed',()=>{
  assert.match(source,/busy=true;error=null;items=\[\];nextOffset=null;completedIds=\[\];/);
  assert.match(source,/alive=false;request\+\+;items=\[\];completedIds=\[\];/);
  assert.match(source,/page\.audience!==viewerRole/);
  assert.match(source,/controller\.getPublishedHubEducation\(organization,0\)/);
  assert.match(source,/filter\(item=>!previous\.has\(item\.id\)\)/);
});
