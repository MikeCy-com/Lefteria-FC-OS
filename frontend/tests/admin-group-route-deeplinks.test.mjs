import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const groups=read('routes/admin/groups/+page.svelte');
const moduleCatalog=read('lib/admin/module-catalog.ts');
const audienceEndpoint=readFileSync(
  new URL('../../backend/src/LefteriaFc.Api/Audiences/AdminAudienceEndpoints.cs',import.meta.url),'utf8');

test('Teams/Academy route responds to repeat deep-link navigation without one-shot mount flags',()=>{
  assert.ok(compile(groups,{filename:'groups/+page.svelte',generate:'server'}).js.code.length>100);
  for(const key of ['group_id','organization_id','view','new'])
    assert.match(groups,new RegExp("page\\.url\\.searchParams\\.get\\('"+key+"'\\)"),key);
  for(const state of ['appliedInitialOrganization','appliedRequestedView','openedRequestedCreate'])
    assert.ok(!groups.includes(state),state+' may not gate later navigation');
  for(const state of ['lastAppliedGroupRouteKey','lastAppliedOrganizationRouteKey',
    'lastAppliedViewRouteKey','lastOpenedCreateRouteKey'])
    assert.ok(groups.includes(state),state);
});

test('group deep links never select an unverified or foreign-organization group',()=>{
  assert.match(groups,/directory\\?\\.organization_id===owner\\?directory\\.groups:\\[\\]/);
  assert.match(groups,/available\\.some\\(group=>group\\.id===requested\\)/);
  assert.match(groups,/const key=actor\\+':'\\+owner\\+':'\\+requested/);
  assert.match(groups,/if\\(lastAppliedGroupRouteKey===key\\)return/);
  assert.match(groups,/if\\(directory\\?\\.organization_id!==owner\\)lastAppliedGroupRouteKey=''/);
  assert.match(groups,/if\\(focusedGroupId && !available\\.some\\(/);
  assert.match(groups,/focusedGroupId=requested/);
});

test('organization deep links stay within signed-in server assignments',()=>{
  assert.match(groups,/if\\(!browser\\|\\|\\$auth\\.status!=='authenticated'\\|\\|!actor\\|\\|!choices\\.length\\)return/);
  assert.match(groups,/choices\\.some\\(item=>item\\.id===requested\\)/);
  assert.match(groups,/organization=requested/);
  assert.match(groups,/if \\(assignments\\.some\\(org => org\\.id === next\\)\\) organization = next as Organization/);
  assert.match(groups,/const canCreate = \\$derived\\(/);
  assert.match(groups,/assignments\\.some\\(org => org\\.id === organization\\)/);
});

test('player CRM and new-group routes require current write grant and fresh owner data',()=>{
  assert.match(groups,/directoryView=requested==='players'&&allowed\\?'players':'groups'/);
  assert.match(groups,/if\\(!browser\\|\\|!selected\\|\\|!actor\\|\\|!loaded\\|\\|!allowed\\)return/);
  assert.match(groups,/directory\\?\\.organization_id===selected/);
  assert.match(groups,/if\\(lastOpenedCreateRouteKey===key\\)return/);
  assert.match(groups,/if\\(!wantsNew\\)\\{lastOpenedCreateRouteKey='';return;\\}/);
  assert.match(groups,/\\{#if canCreate && directoryView==='players' && \\$auth\\.session\\}/);
  assert.match(groups,/\\{#if canCreate\\}/);
});

test('no client URL navigation can bypass the API owner/role permission boundary',()=>{
  assert.match(moduleCatalog,/if \\(item\\.id === 'groups' \\|\\| item\\.id === 'fixtures' \\|\\| item\\.id === 'territories'\\)/);
  assert.match(audienceEndpoint,/AdminPermissions\\.Has\\(session\\.User\\.Role,capability\\)/);
  assert.match(audienceEndpoint,/session\\.User\\.Organizations\\.Any\\(org => org\\.Id==organization_id\\)/);
  assert.match(groups,/\\/api\\/admin\\/football\\/audiences\\?/);
  assert.doesNotMatch(groups,/localStorage|sessionStorage/);
});
