import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115270000_AdminExactGroupRoster.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminRosterPlayerEndpoints.cs');
const service=read('backend/src/LefteriaFc.Infrastructure/Audiences/PostgresAdminRosterPlayerDirectory.cs');
const contract=read('backend/src/LefteriaFc.Application/Audiences/AdminRosterPlayerContracts.cs');
const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
const workspace=read('frontend/src/lib/admin/AdminGroupWorkspace.svelte');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const probe=read('ops/postgres/admin-group-roster-boundary.sql');
const checks=read('ops/postgres/web-admin-auth-permissions-check.sql');

test('all group player views are exact paged Academy or First Team groups from canonical database',()=>{
  for(const m of ['club.teams t','club.academy_groups a','club.player_academy_groups m',
    'p.primary_academy_group_id=_group','m.academy_group_id=_group','p.team_id=_group',
    'identity.lock_admin_club_authority(_digest,_owner)','club.admin_football_allowed',
    'club.football_operational_scopes','club.academy_group_location_assignments',
    'v_actor,v_role,_owner,v_scope,v_location,false','jsonb_build_array(_group)']){
    if(m==='FOR UPDATE')continue;
    assert.ok(sql.includes(m),m);
  }
  assert.match(sql,/LANGUAGE plpgsql VOLATILE SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/NOT p\.is_archived AND NOT p\.is_transferred/);
  assert.match(sql,/roster_import_state/);
  assert.match(sql,/'group_id',_group/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.read_admin_group_roster/);
  assert.doesNotMatch(sql,/SELECT .*parent_phone|guardian_phone|birth_date|UPDATE club\.players/);
  assert.match(probe,/f\.pronargs<>5/);
  assert.match(probe,/p\.team_id=_group/);
  assert.match(probe,/v_actor,v_role,_owner,v_scope,v_location,false/);
  assert.match(probe,/jsonb_build_array\(_group\)/);
  assert.match(probe,/has_any_column_privilege\(admin_role,c\.oid/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_admin_group_roster/);
  assert.match(checks,/admin-group-roster-boundary\.sql/);
});
test('new endpoint is manager-only and excluded from public frozen OpenAPI contract',()=>{
  assert.match(api,/MapGet\("\/api\/admin\/football\/group-players"/);
  assert.match(api,/context\.Request\.Query\["group_id"\]\.Count!=1/);
  assert.match(api,/ReadGroupAsync\(/);
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role,capability\)/);
  assert.match(api,/ExcludeFromDescription\(\)/);
  assert.match(contract,/record AdminGroupRosterPageDto\(string OrganizationId,string GroupId/);
  assert.match(service,/result\.GroupId!=groupId/);
  assert.match(service,/p\.TeamId!=groupId/);
  assert.match(service,/!p\.AcademyGroupIds\.Contains\(groupId\)/);
  assert.match(service,/secrets\.TokenDigest\(cookie\)/);
});
test('in-group player view defaults to selected group and assignment search is deliberately separate',()=>{
  assert.ok(compile(roster,{filename:'AdminRosterPlayersPanel.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(workspace,{filename:'AdminGroupWorkspace.svelte',generate:'server'}).js.code.length>100);
  assert.match(workspace,/focusGroupId=\{group\.id\}/);
  assert.match(roster,/rosterMode=\$state<'group'\|'assign'>\('group'\)/);
  assert.match(roster,/currentGroup=\$derived\(focusGroupId&&\(rosterMode==='group'\|\|!allowAssignments\)/);
  assert.match(roster,/\/api\/admin\/football\/group-players\?/);
  assert.match(roster,/data-testid="group-player-scope"/);
  assert.match(roster,/data-testid="group-current-player-filter"/);
  assert.match(roster,/data-testid="group-assign-player-filter"/);
  assert.match(roster,/response\.group_id!==group/);
  assert.match(roster,/p\.team_id!==group/);
  assert.match(roster,/academy_group_ids as string\[\]\)\.includes\(group\)/);
  assert.match(roster,/expected_group_ids:previous,group_ids:desired/);
  assert.match(roster,/data-testid="admin-player-billing-link"/);
  assert.match(roster,/AdminPlayerPrivateRecordsPanel/);
  assert.doesNotMatch(roster,/Math\.random|localStorage|sessionStorage|innerHTML/);
});
