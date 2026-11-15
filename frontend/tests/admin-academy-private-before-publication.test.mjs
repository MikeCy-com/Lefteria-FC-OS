import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115962000_AcademyGroupPrivateByDefault.cs');
const editor=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110170000_AcademyGroupProfileEditor.cs');
const publicDirectory=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115220000_PublicAcademyVerifiedTerritories.cs');
const httpChecks=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminHttpChecks.RosterTerritories.cs');
const fixture=read('backend/tests/LefteriaFc.Identity.IntegrationTests/WebAdminDatabaseFixture.AnnouncementTerritories.cs');

test('only newly inserted Academy profiles default to private, with reversible DDL',()=>{
  assert.match(migration,/ALTER TABLE club\.academy_group_profiles[\s\S]*ALTER COLUMN is_public SET DEFAULT false/);
  assert.match(migration,/ALTER COLUMN is_public SET DEFAULT true/);
  assert.doesNotMatch(migration,/UPDATE club\.academy_group_profiles|DELETE FROM club\.academy_group_profiles|DROP TABLE/);
  assert.match(editor,/INSERT INTO club\.academy_group_profiles\(/);
});

test('public Academy directory retains active location plus explicit publication checks',()=>{
  assert.match(publicDirectory,/WHERE g\.organization_id='lefteria_fc_academy' AND p\.is_public/);
  assert.match(publicDirectory,/l\.is_active AND c\.is_active/);
});

test('real PostgreSQL HTTP check covers approval, public listing and withdrawal',()=>{
  assert.match(fixture,/AND p\.revision=1 AND NOT p\.is_public/);
  assert.match(fixture,/AcademyGroupVisiblePubliclyAsync/);
  assert.match(httpChecks,/CHECK scoped create: deliberate public approval/);
  assert.match(httpChecks,/SetVisibility\(client,login\.Cookie,groupId!,1,true\)/);
  assert.match(httpChecks,/SetVisibility\(client,login\.Cookie,groupId!,2,false\)/);
  assert.match(httpChecks,/Unapproved new Academy group was published by default/);
  assert.match(httpChecks,/Withdrawn Academy group leaked through public listing/);
});
