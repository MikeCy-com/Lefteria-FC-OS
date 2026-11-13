import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {prioritizeUncompletedPublishedEducation,searchPublishedHubEducation} from '../src/lib/mobile/published-education.ts';

const article=(id,audience='parent',organization_id='lefteria_fc_academy')=>
  ({id,organization_id,audience,title:id,body:'A safe educational article',
    published_at:'2026-11-13T10:00:00Z',age_min:null,age_max:null});
const component=readFileSync(new URL('../src/lib/mobile/components/MobilePublishedHubEducation.svelte',import.meta.url),'utf8');

test('learning pathway brings unfinished approved items first, without modifying the authorized list',()=>{
  const loaded=[article('first'),article('second'),article('third'),article('fourth')];
  const ordered=prioritizeUncompletedPublishedEducation(loaded,['second','fourth']);
  assert.deepEqual(ordered.map(x=>x.id),['first','third','second','fourth']);
  assert.deepEqual(loaded.map(x=>x.id),['first','second','third','fourth']);
});
test('unknown completion IDs cannot insert articles; filtered results remain bounded to input',()=>{
  const loaded=[article('alpha'),article('beta','player'),article('gamma')];
  const filtered=searchPublishedHubEducation(loaded,'gamma');
  const ordered=prioritizeUncompletedPublishedEducation(filtered,['secret-other-organization','gamma']);
  assert.deepEqual(ordered.map(x=>x.id),['gamma']);
  assert.equal(ordered.every(item=>filtered.includes(item)),true);
  assert.deepEqual(prioritizeUncompletedPublishedEducation([],['guess']),[]);
});
test('role and organization fetch boundaries remain unchanged in pathway UI',()=>{
  assert.match(component,/const visibleItems=\$derived\(searchPublishedHubEducation\(items,search\)\)/);
  assert.match(component,/prioritizeUncompletedPublishedEducation\(visibleItems,completedIds\)/);
  assert.match(component,/\{#each learningOrder as item \(item\.id\)\}/);
  assert.match(component,/page\.audience!==viewerRole/);
  assert.match(component,/busy=true;error=null;items=\[\];nextOffset=null;completedIds=\[\];/);
  assert.doesNotMatch(component,/localStorage|sessionStorage|AI_PROVIDER|payment/);
});
