import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const groups=read('routes/admin/groups/+page.svelte');
const moduleCatalog=read('lib/admin/module-catalog.ts');
const audienceEndpoint=readFileSync(
  new URL('../../backend/src/LefteriaFc.Api/Audiences/AdminAudienceEndpoints.cs',import.meta.url),'utf8');

test('Teams/Academy route responds to repeat URL navigation, not just the first page mount',()=>{
  assert.ok(compile(groups,{filename:'groups/+page.svelte',generate:'server'}).js.code.length>100);
  for(const key of ['group_id','organization_id','view','new'])
    assert.ok(groups.includes("page.url.searchParams.get('"+key+"')"),key);
  for(const oldFlag of ['appliedInitialOrganization','appliedRequestedView','openedRequestedCreate'])
    assert.ok(!groups.includes(oldFlag),'One-shot flag '+oldFlag+' would block later deep links');
  for(const marker of ['lastAppliedGroupRouteKey','lastAppliedOrganizationRouteKey',
    'lastAppliedViewRouteKey','lastOpenedCreateRouteKey'])
    assert.ok(groups.includes(marker),marker);
});

test('a linked group must be present in the current verified organization directory',()=>{
  for(const text of [
    "const available=directory?.organization_id===owner?directory.groups:[];",
    "available.some(group=>group.id===requested)",
    "const key=actor+':'+owner+':'+requested;",
    "if(lastAppliedGroupRouteKey===key)return;",
    "if(directory?.organization_id!==owner)lastAppliedGroupRouteKey='';",
    "focusedGroupId=requested;"
  ]) assert.ok(groups.includes(text),text);
});

test('query-selected organizations must belong to the authenticated session',()=>{
  for(const text of [
    "if(!browser||$auth.status!=='authenticated'||!actor||!choices.length)return;",
    "choices.some(item=>item.id===requested)",
    "const key=actor+':'+choices.map(org=>org.id).join(',')+':'+(requested??'');",
    "organization=requested;",
    "if (assignments.some(org => org.id === next)) organization = next as Organization;",
    "assignments.some(org => org.id === organization)"
  ]) assert.ok(groups.includes(text),text);
});

test('player CRM and group creation routes must never bypass current write access',()=>{
  for(const text of [
    "directoryView=requested==='players'&&allowed?'players':'groups';",
    "if(!browser||!selected||!actor||!loaded||!allowed)return;",
    "const selected=organization,loaded=directory?.organization_id===selected;",
    "if(!wantsNew){lastOpenedCreateRouteKey='';return;}",
    "if(lastOpenedCreateRouteKey===key)return;",
    "{#if canCreate && directoryView==='players' && $auth.session}",
    "{#if canCreate}"
  ]) assert.ok(groups.includes(text),text);
});

test('server audience owner/role authorization remains the final access control',()=>{
  assert.ok(moduleCatalog.includes("if (item.id === 'groups' || item.id === 'fixtures' || item.id === 'territories')"));
  assert.ok(audienceEndpoint.includes("AdminPermissions.Has(session.User.Role,capability)"));
  assert.ok(audienceEndpoint.includes("session.User.Organizations.Any(org => org.Id==organization_id)"));
  assert.ok(groups.includes("'/api/admin/football/audiences?'"));
  assert.doesNotMatch(groups,/localStorage|sessionStorage/);
});
