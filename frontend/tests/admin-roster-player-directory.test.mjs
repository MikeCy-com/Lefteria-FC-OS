import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const src=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const sql=src('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108133000_AdminRosterPlayerDirectory.cs');
const api=src('backend/src/LefteriaFc.Api/Audiences/AdminRosterPlayerEndpoints.cs');
const ui=src('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
const page=src('frontend/src/routes/admin/groups/+page.svelte');
const grant=src('ops/postgres/web-admin-auth-grants.sql');
const border=src('ops/postgres/admin-player-directory-boundary.sql');

test('private player picker is manager-only, owner-locked, paged and intentionally minimal',()=>{
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/club\.roster_import_state/);
  assert.match(sql,/p\.is_active/);
  assert.match(sql,/NOT p\.is_archived AND NOT p\.is_transferred/);
  assert.match(sql,/club\.player_academy_groups/);
  assert.match(sql,/\bUNION\b/);
  assert.match(sql,/LIMIT _limit OFFSET _offset/);
  assert.match(sql,/RETURN jsonb_build_object/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.read_admin_roster_players/);
  assert.match(grant,/GRANT EXECUTE ON FUNCTION club\.read_admin_roster_players/);
  assert.match(border,/NOT has_function_privilege\(admin_role,f\.oid,'EXECUTE'\)/);
  assert.match(border,/has_any_column_privilege\(admin_role,c\.oid,'SELECT,INSERT,UPDATE,REFERENCES'\)/);
  assert.doesNotMatch(sql,/'parent_name'|'phone'|'birth_date'|'source_json'|'emergency_contact'/);
  assert.doesNotMatch(api,/MapPost\(|MapPut\(|MapDelete\(/);
  assert.match(api,/\.RequireAuthorization\(WebAdminServices.Policy\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role,capability\)/);
});

test('admin player list uses current backend membership, not fake data or stale owner',()=>{
  assert.match(page,/AdminRosterPlayersPanel/);
  assert.match(page,/\{#if canCreate && organization && \$auth\.session\}/);
  assert.match(page,/\{#key organization \+ ':' \+ \$auth\.session\.user\.id/);
  assert.match(ui,/data-testid="admin-private-player-directory"/);
  assert.match(ui,/data-testid="admin-players-list"/);
  assert.match(ui,/fetch\('\/api\/admin\/football\/players\?'/);
  assert.match(ui,/credentials:'same-origin',cache:'no-store'/);
  assert.match(ui,/controller\.abort\(\)/);
  assert.match(ui,/response\.status===401/);
  assert.match(ui,/response\.status===403/);
  assert.match(ui,/verified\(json,currentOwner,currentOffset\)/);
  assert.match(ui,/academy_group_ids/);
  assert.match(ui,/groupLabels\(player\)/);
  assert.match(ui,/Η τροποποίηση αναθέσεων δεν έχει ενεργοποιηθεί/);
  assert.doesNotMatch(ui,/\{@html|innerHTML|fetch\([^\n]*method:'POST'|\bmockPlayers\b/);
});
