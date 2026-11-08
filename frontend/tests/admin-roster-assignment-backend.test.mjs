import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const base=new URL('../../',import.meta.url);
const read=p=>readFileSync(new URL(p,base),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108140000_AdminRosterAssignment.cs');
const api=read('backend/src/LefteriaFc.Api/Audiences/AdminRosterAssignmentEndpoints.cs');
const writer=read('backend/src/LefteriaFc.Infrastructure/Audiences/PostgresAdminRosterAssignmentWriter.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const perms=read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary=read('ops/postgres/admin-roster-assignment-boundary.sql');

test('roster assignments lock real organization authority, compare membership and audit atomically',()=>{
  assert.match(sql,/PERFORM identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/FOR UPDATE/);
  assert.match(sql,/IF v_current<>v_expected THEN/);
  assert.match(sql,/ERRCODE='40001'/);
  assert.match(sql,/UPDATE club\.players SET team_id/);
  assert.match(sql,/UPDATE club\.players SET primary_academy_group_id/);
  assert.match(sql,/DELETE FROM club\.player_academy_groups/);
  assert.match(sql,/INSERT INTO club\.player_academy_groups/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.doesNotMatch(sql,/INSERT INTO (messages|chat|communications)\./);
});

test('narrow guarded API and SQL execute boundary exclude direct roster privileges',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role, capability\)/);
  assert.match(api,/JsonUnmappedMemberHandling\.Disallow/);
  assert.match(api,/keys\.Add\(property\.Name\)/);
  assert.match(writer,/secrets\.TokenDigest\(cookie\)/);
  assert.match(writer,/NpgsqlDbType\.Jsonb/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.write_admin_roster_assignment/);
  assert.match(grants,/admin-roster-assignment-boundary\.sql/);
  assert.match(perms,/admin-roster-assignment-boundary\.sql/);
  assert.match(boundary,/has_any_column_privilege\(admin_role,c\.oid,'SELECT,INSERT,UPDATE,REFERENCES'\)/);
});
