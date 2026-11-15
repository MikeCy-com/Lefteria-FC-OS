import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115270000_GroupScopedPlayerDirectory.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const checks=read('ops/postgres/web-admin-auth-permissions-check.sql');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminRosterPlayerEndpoints.cs');
const store=read('backend/src/LefteriaFc.Infrastructure/Audiences/PostgresAdminRosterPlayerDirectory.cs');
const contract=read('backend/src/LefteriaFc.Application/Audiences/AdminRosterPlayerContracts.cs');
const workspace=read('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
const player=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');

test('group roster is a separate exact-group, manager/grant/session locked read',()=>{
  assert.match(sql,/CREATE FUNCTION club\.read_admin_group_players\(/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/club\.teams g/);
  assert.match(sql,/club\.academy_groups g/);
  assert.match(sql,/p\.team_id=_group/);
  assert.match(sql,/p\.primary_academy_group_id=_group/);
  assert.match(sql,/membership\.academy_group_id=_group/);
  assert.equal((sql.match(/membership\.academy_group_id=_group/g)||[]).length,2,
    'Both total and page must filter inside SQL, not after LIMIT');
  assert.match(sql,/LIMIT _limit OFFSET _offset/);
  assert.match(sql,/'group_id',_group/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.read_admin_group_players/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_admin_group_players/);
  assert.match(checks,/club\.read_admin_group_players\(text,text,text,integer,integer\)/);
  assert.doesNotMatch(sql,/'phone'|'birth_date'|'medical'|'guardian'|'source_json'|'parent_name'/);
});
test('API uses the private first-party admin cookie, returns exact-group DTO and no writes',()=>{
  assert.match(api,/MapGet\("\/api\/admin\/football\/group-players"/);
  assert.match(api,/context\.Request\.Query\["group_id"\]\.Count!=1/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role,capability\)/);
  assert.match(api,/ReadGroupAsync\(/);
  assert.match(api,/\.RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.doesNotMatch(api,/MapPost\(|MapPut\(|MapDelete\(/);
  assert.match(store,/JsonSerializer\.Deserialize<AdminGroupRosterPlayersPageDto>/);
  assert.match(store,/result\.GroupId!=groupId/);
  assert.match(store,/!p\.AcademyGroupIds\.Contains\(groupId\)/);
  assert.match(contract,/public sealed record AdminGroupRosterPlayersPageDto/);
});
test('group workspace shows only group members and offers separate authorized same-owner intake',()=>{
  assert.doesNotThrow(()=>compile(player,{filename:'AdminRosterPlayersPanel.svelte',generate:'server'}));
  assert.doesNotThrow(()=>compile(workspace,{filename:'AdminGroupWorkspace.svelte',generate:'server'}));
  assert.match(workspace,/groupId=\{group\.id\}/);
  assert.match(player,/data-testid="admin-group-members-view"/);
  assert.match(player,/data-testid="admin-group-add-player-view"/);
  assert.match(player,/fetch\('\/api\/admin\/football\/group-players\?'/);
  assert.match(player,/params\.set\('group_id',focusedGroup\)/);
  assert.match(player,/json\.group_id!==focusedGroup/);
  assert.match(player,/json\.players\.some\(player=>!currentGroups\(player\)\.includes\(focusedGroup\)\)/);
  assert.match(player,/method:'PUT',credentials:'same-origin',cache:'no-store'/);
  assert.match(player,/expected_group_ids:previous,group_ids:desired/);
  assert.match(player,/owner==='lefteria_fc'/);
  assert.match(player,/data-testid="admin-private-player-directory"/);
  assert.doesNotMatch(player,/@html|innerHTML|localStorage/);
});
