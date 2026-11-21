import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read=path=>readFileSync(new URL(path,import.meta.url),'utf8');
const migration=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115990000_AcademyGuardianDelegationLineage.cs');
const base=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110173000_MultipleGuardianPermissions.cs');
const previous=read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115970000_AcademyGuardianOtpScope.cs');
const fixture=read('../../ops/postgres/academy-enrollment-checks.sql');
const mobile=read('../../backend/src/LefteriaFc.Application/MobileAuth/MobileAuthService.cs');
const family=read('../../backend/src/LefteriaFc.Infrastructure/Family/PostgresParentFamilyQueries.cs');
const calendar=read('../../backend/src/LefteriaFc.Infrastructure/ClubCalendar/OperationalScopeSql.cs');

const up=migration.split('protected override void Down')[0];
const down=migration.split('protected override void Down')[1];

test('only an exact live Academy original OTP claim can anchor new guardian lineage',()=>{
  assert.match(up,/CREATE OR REPLACE FUNCTION identity\.valid_guardian_phone_grants\(\)/);
  assert.match(up,/CREATE OR REPLACE FUNCTION identity\.require_canonical_guardian_delegation_root\(\)/);
  assert.match(up,/WITH RECURSIVE roots\(/);
  assert.match(up,/identity\.current_academy_guardian_claims\(issuer\.normalized_phone\)/);
  assert.match(up,/identity\.current_academy_guardian_claims\(original\.normalized_phone\)/);
  assert.match(up,/root\.player_id=grant_row\.player_id/);
  assert.match(up,/root\.organization_id=grant_row\.organization_id/);
  assert.match(up,/claim\.player_id=NEW\.player_id/);
  assert.match(up,/claim\.organization_id=NEW\.organization_id/);
  assert.match(up,/NEW\.source_permission_id IS NULL/);
  assert.match(up,/original\.id=NEW\.issuer_identity_id/);
  assert.match(up,/original\.role='parent' AND original\.is_active/);
  assert.match(up,/root\.organization_id='lefteria_fc_academy'/);
  assert.match(up,/grant_row\.status='granted'/);
  assert.match(up,/valid\.depth<20/);
  assert.match(up,/NOT grant_row\.id=ANY\(valid\.path\)/);
  assert.match(up,/valid\.path\|\|grant_row\.id/);
  assert.match(up,/issuer\.normalized_phone=valid\.target_phone/);
  assert.match(up,/blocked\.role IN \('player','coach','management'\)/);
  assert.match(up,/REVOKE ALL ON FUNCTION identity\.valid_guardian_phone_grants/);
  assert.match(up,/REVOKE ALL ON FUNCTION identity\.require_canonical_guardian_delegation_root/);
  assert.doesNotMatch(up,/INSERT INTO identity\.mobile_identities|INSERT INTO identity\.canonical_contact_bindings/);
});

test('inherited grant and revocation services remain the single authority for multiple children',()=>{
  assert.match(base,/identity\.issue_guardian_permission/);
  assert.match(base,/identity\.issue_many_guardian_permissions|identity\.current_phone_granted_children/);
  assert.match(base,/source_permission_id/);
  assert.match(base,/WITH RECURSIVE affected/);
  assert.match(base,/g\.source_permission_id=affected\.id/);
  assert.match(family,/identity\.current_phone_granted_children/);
  assert.match(calendar,/identity\.current_phone_granted_children/);
  assert.match(mobile,/challenge\.Verify/);
  assert.match(mobile,/EnsureGrantedParentIdentityAsync/);
  assert.ok(mobile.indexOf('challenge.Verify(')<mobile.indexOf('EnsureGrantedParentIdentityAsync'));
  assert.match(previous,/CREATE FUNCTION identity\.current_academy_guardian_claims/);
  assert.match(previous,/claim\.expires_at>statement_timestamp\(\)/);
});

test('rolling back never silently loses an issued new Academy original guardian lineage',()=>{
  assert.match(down,/academy_guardian_delegation_requires_forward_recovery/);
  assert.match(down,/identity\.academy_guardian_otp_claims/);
  assert.match(down,/CREATE OR REPLACE FUNCTION identity\.valid_guardian_phone_grants/);
  assert.match(down,/JOIN identity\.mobile_record_links original_link/);
  assert.doesNotMatch(down,/CREATE FUNCTION identity\.current_academy_guardian_claims/);
  assert.match(down,/CREATE OR REPLACE FUNCTION identity\.require_canonical_guardian_delegation_root/);
  assert.doesNotMatch(down,/JOIN identity\.current_academy_guardian_claims\(original\.normalized_phone\)/);
});

test('disposable database exercises OTP eligibility, recipient phone, lineage and revocation',()=>{
  for(const marker of [
    'Unissued second-guardian phone already had child access',
    'Verified Academy original guardian could not grant second parent',
    'Original guardian grant replay is not idempotent',
    'An unverified phone became a logged-in parent',
    'Second verified parent not linked to exact child',
    'Delegated guardian fabricated an unanchored root',
    'Nested second guardian lost revocation lineage',
    'Revoked second-guardian lineage remained authorized',
    'Explicit second-guardian regrant failed',
    'Second guardian gained a child outside this claim',
    'Original proof revoked but second guardian retained access',
    'Revoked original guardian could issue a new grant',
    'ROLLBACK;'
  ]) assert.ok(fixture.includes(marker),'Missing fixture: '+marker);
  assert.ok(fixture.indexOf('Verified Academy original guardian could not grant second parent')<
    fixture.indexOf("guardian_proof(admin_digest,'revoke'"));
  assert.ok(fixture.indexOf('Original proof revoked but second guardian retained access')>
    fixture.indexOf("guardian_proof(admin_digest,'revoke'"));
});
