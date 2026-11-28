import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('canonical group rename requires exact territory permission and updates only group names',()=>{
  const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128190000_AdminGroupRename.cs');
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/club\.require_admin_group_profile_scope\(_digest,_owner,_group,true\)/);
  assert.match(sql,/v_previous<>_expected/);
  assert.match(sql,/a\.location_id IS NOT DISTINCT FROM v_location/);
  assert.match(sql,/UPDATE club\.teams SET name=_name WHERE organization_id=_owner AND id=_group/);
  assert.match(sql,/UPDATE club\.academy_groups SET name=_name WHERE organization_id=_owner AND id=_group/);
  assert.match(sql,/admin\.roster\.group\.rename/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.rename_admin_group/);
  assert.doesNotMatch(sql,/UPDATE club\.players|DELETE FROM|SET id=/);
  const api=read('backend/src/LefteriaFc.Api/Audiences/AdminGroupRenameEndpoints.cs');
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/group_rename_conflict/);
  assert.match(api,/group_rename_denied/);
  const grants=read('ops/postgres/web-admin-auth-grants.sql');
  assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION club.rename_admin_group(text,text,text,text,text) TO :"admin_auth_role"'));
  assert.ok(grants.includes('REVOKE ALL ON FUNCTION club.rename_admin_group(text,text,text,text,text) FROM PUBLIC, :"app_role"'));
});
test('name editor retains verified identity and refreshes the exact group directory',()=>{
  const source=read('frontend/src/lib/admin/AdminGroupNameEditor.svelte');
  assert.ok(compile(source,{filename:'AdminGroupNameEditor.svelte',generate:'server'}).js.code.length>100);
  assert.match(source,/expected_name:expected,name:next/);
  assert.match(source,/result\.organization_id!==org\|\|result\.id!==scope\|\|result\.name!==next/);
  assert.match(source,/onChanged\(\)/);
  const workspace=read('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
  assert.match(workspace,/<AdminGroupNameEditor owner=\{organization\} groupId=\{group\.id\} groupName=\{group\.name\}/);
  assert.match(workspace,/actorId=\{user\.id\} allowed=\{access\.settings\}/);
});

test('group banner picker uses only the existing organization media library and explicit saves',()=>{
  for(const name of ['AdminAcademyGroupProfilePanel','AdminFirstTeamProfilePanel']){
    const source=read('frontend/src/lib/admin/'+name+'.svelte');
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
    assert.ok(source.includes('<PublishedMediaBrowser organization={owner} embedded onPick='));
    assert.ok(source.includes('asset.organization_id!==owner'));
    assert.ok(source.includes('bannerUrl=asset.url;pickedBanner=asset.url'));
    assert.ok(source.includes('bannerUrl===record?.banner_url||bannerUrl===pickedBanner'));
    assert.ok(source.includes('referrerpolicy="no-referrer"'));
    assert.ok(source.includes('banner_url:bannerUrl'));
  }
});
