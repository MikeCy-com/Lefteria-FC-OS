import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
const root=new URL('../../',import.meta.url);
const read=path=>readFileSync(new URL(path,root),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108210000_MobileChatGroupDirectory.cs');
const api=read('backend/src/LefteriaFc.Api/MobileChat/MobileChatDirectoryEndpoints.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const checks=read('ops/postgres/identity-app-permissions-check.sql');
const service=read('backend/src/LefteriaFc.Application/MobileChat/MobileChatDirectory.cs');
const tests=read('backend/tests/LefteriaFc.Identity.IntegrationTests/MobileChatDirectoryHttpChecks.cs');

test('mobile chat scope recomputes verified roster membership in SQL rather than accepting group selector',()=>{
  for(const proof of [
    'identity.mobile_identities','identity.mobile_record_links',
    'identity.canonical_contact_bindings','club.players',
    'club.player_academy_groups','primary_academy_group_id',
    'club.staff_teams','club.staff_academy_groups','club.academy_group_coaches',
    'identity.admin_organization_grants','active_organization_id',
    'p.is_active AND NOT p.is_archived AND NOT p.is_transferred',
    "_role NOT IN ('parent','player','coach','management')"
  ])assert.ok(sql.includes(proof),proof);
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/REVOKE ALL ON FUNCTION club\.read_mobile_chat_groups/);
  assert.doesNotMatch(sql,/INSERT INTO (chat|messages|notifications|outbox)\./);
});

test('runtime only executes security scoped function, and endpoint is protected',()=>{
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_mobile_chat_groups/);
  assert.match(checks,/has_function_privilege\(current_user,[\s\S]*?'club\.read_mobile_chat_groups/);
  assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
  assert.match(api,/MobilePrincipal\.Read\(context\.User\)/);
  assert.match(service,/AuthorizeCurrentIdentityAsync/);
  assert.match(tests,/AccountImportFixture input/);
  assert.match(tests,/GetAsync\(fan,ChatDirectoryPath,403\)/);
  assert.match(tests,/GetAsync\(parent,ChatDirectoryPath,200\)/);
  assert.match(tests,/GetAsync\(player,ChatDirectoryPath,200\)/);
  assert.match(tests,/GetAsync\(coach,ChatDirectoryPath,200\)/);
  assert.match(tests,/GetAsync\(management,ChatDirectoryPath,200\)/);
  assert.match(tests,/AssertGroups/);
});
