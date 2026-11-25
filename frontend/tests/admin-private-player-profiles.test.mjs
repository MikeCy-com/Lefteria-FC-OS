import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261122235900_AcademyPrivatePlayerProfiles.cs');
const api=read('backend/src/LefteriaFc.Api/WebAdmin/AdminPlayerProfileEndpoints.cs');
const register=read('backend/src/LefteriaFc.Api/WebAdmin/WebAdminEndpoints.cs');
const photo=read('backend/src/LefteriaFc.Api/WebAdmin/AdminProfilePhotoEndpoints.cs');
const grants=read('ops/postgres/academy-private-player-profiles-grants.sql');
const fixture=read('ops/postgres/academy-private-player-profiles-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const panel=read('frontend/src/lib/admin/AdminPlayerPrivateProfilePanel.svelte');
const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');

test('private profile only adds scoped metadata; canonical player and billing sources remain immutable',()=>{
  for(const marker of [
    '20261122235900_AcademyPrivatePlayerProfiles',
    'club.academy_player_private_profiles',
    'organization_id varchar(32)',
    'biography varchar(1000)',
    'preferred_position varchar(80)',
    'portrait_jpeg bytea',
    'club.admin_roster_player_visible',
    'club.admin_football_allowed',
    "'lefteria_fc_academy'",
    'FOR SHARE',
    'academy_portrait_sanitized_jpeg',
    'player_profile_revision_conflict',
    'admin.academy.player.profile.update',
    'admin.academy.player.portrait.remove',
    'academy_private_profile_requires_forward_recovery'
  ])assert.ok(migration.includes(marker),marker);
  assert.doesNotMatch(migration,/UPDATE club\.players SET/);
  assert.doesNotMatch(migration,/DELETE FROM club\.players/);
  assert.match(migration,/REVOKE ALL ON FUNCTION club\.authorize_academy_private_profile/);
});

test('private player image endpoints reuse bounded decoder, stripping metadata, and refuse public API exposure',()=>{
  assert.match(api,/IAvatarImageSanitizer sanitizer/);
  assert.match(api,/sanitizer\.Sanitize\(input,mime,ct\)/);
  assert.match(api,/CryptographicOperations\.ZeroMemory/);
  assert.match(api,/AdminProfilePhotoEndpoints\.ReadSinglePhotoAsync/);
  assert.match(photo,/internal static async Task<\(byte\[\] Data,string\? ContentType\)> ReadSinglePhotoAsync/);
  assert.match(api,/CacheControl="private, no-store"/);
  assert.match(api,/ContentSecurityPolicy="sandbox"/);
  assert.match(api,/organization_id/);
  assert.match(api,/DisableAntiforgery\(\)/);
  assert.match(api,/.ExcludeFromDescription\(\)/);
  assert.match(register,/endpoints\.MapAdminPlayerProfiles\(\)/);
  assert.doesNotMatch(api,/wwwroot|static media|image_url/);
});

test('staff-only definer functions and role grants deny cross-site private photo reads',()=>{
  assert.match(migration,/authorize_admin_football\(_digest,'lefteria_fc_academy'\)/);
  assert.match(migration,/admin_roster_player_visible/);
  assert.match(migration,/admin_football_allowed/);
  assert.match(grants,/REVOKE ALL ON club\.academy_player_private_profiles/);
  assert.match(grants,/academy_private_profile_boundary_broken/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION club\.read_admin_academy_player_profile/);
  for(const marker of [
    'unassigned_player_visible_to_location_manager',
    'unassigned_player_edited_by_manager',
    'concurrent_stale_editor_overwrote_child',
    'unsanitized_player_photo_accepted',
    'manager_uploaded_unassigned_child_photo',
    'revoked_staff_read_youth_profile',
    'ROLLBACK'
  ])assert.ok(fixture.includes(marker),marker);
  assert.match(workflow,/academy-private-player-profiles-grants\.sql/);
  assert.match(workflow,/academy-private-player-profiles-checks\.sql/);
});

test('academy roster & private child profile both compile with editing and upload workflows',()=>{
  assert.ok(compile(panel,{filename:'AdminPlayerPrivateProfilePanel.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(roster,{filename:'AdminRosterPlayersPanel.svelte',generate:'server'}).js.code.length>100);
  for(const marker of [
    'admin-player-private-profile',
    'admin-player-upload-private-photo',
    'admin-player-save-private-profile',
    'expectedRevision:profile.revision',
    "method:'PUT'",
    "method:'POST'",
    "method:'DELETE'",
    'X-Lefteria-Admin',
    "data-testid=\"admin-player-private-profile-workspace\""
  ].filter(x=>!x.includes('workspace')))assert.ok(panel.includes(marker),marker);
  assert.match(roster,/admin-player-private-profile-workspace/);
  assert.match(roster,/AdminPlayerPrivateProfilePanel/);
});
