import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const migration = read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115970000_AcademyGuardianOtpScope.cs');
const service = read('../../backend/src/LefteriaFc.Application/MobileAuth/MobileAuthService.cs');
const ports = read('../../backend/src/LefteriaFc.Application/MobileAuth/MobileAuthPorts.cs');
const repository = read('../../backend/src/LefteriaFc.Infrastructure/Identity/PostgresMobileAuthRepository.cs');
const grants = read('../../ops/postgres/guardian-permission-grants.sql');
const boundary = read('../../ops/postgres/guardian-permission-boundary.sql');
const fixture = read('../../ops/postgres/academy-enrollment-checks.sql');

test('new Academy guardian role emerges only through reviewed proof and successful OTP',()=>{
  assert.match(service,/HasPendingAcademyGuardianProofAsync/);
  assert.match(service,/academyProofPending = await transaction/);
  assert.match(service,/predictedRoles\.Add\(MobileRole\.Parent\)/);
  assert.match(service,/if \(academyProofPending\)/);
  assert.match(service,/otpProtector\.ComputeDigest\(stored\.Id, phone, code\)/);
  assert.ok(service.indexOf('var outcome = challenge.Verify') <
    service.indexOf('if (academyProofPending)\n        {\n            await transaction.ConfirmAcademyGuardianOtpAsync'));
  assert.match(service,/await transaction\.EnsureGrantedParentIdentityAsync/);
  assert.match(ports,/Task<int> ConfirmAcademyGuardianOtpAsync/);
  assert.match(repository,/lockedChallenges\.ContainsKey\(challengeId\)/);
  assert.match(repository,/identity\.confirm_academy_guardian_otp/);
  assert.match(repository,/identity\.has_pending_academy_guardian_proof/);
});

test('human review, phone OTP and current exact-child scope are independent boundaries',()=>{
  for(const s of [
    'CREATE TABLE identity.academy_guardian_otp_claims',
    'ENABLE ROW LEVEL SECURITY',
    'academy_guardian_otp_claims_immutable',
    'CREATE FUNCTION identity.has_pending_academy_guardian_proof',
    'CREATE FUNCTION identity.confirm_academy_guardian_otp',
    'code_digest IS DISTINCT FROM _code_digest',
    "octet_length(_code_digest)<>32",
    'otp.created_at>=proof.reviewed_at',
    'CREATE FUNCTION identity.current_academy_guardian_claims',
    "proof.status='verified_proof'",
    'claim.expires_at>statement_timestamp()',
    "application.status='approved'",
    "application.recorded_by IS NULL",
    'registration_consents',
    'identity.current_phone_granted_children',
    'identity.has_current_guardian_phone_grant',
    'require_canonical_guardian_delegation_root',
    'guardian_otp_claim_history_requires_forward_recovery'
  ]) assert.ok(migration.includes(s),'Migration missing '+s);
  assert.doesNotMatch(migration,/INSERT INTO identity\.canonical_contact_bindings|INSERT INTO identity\.mobile_record_links/);
  assert.doesNotMatch(migration,/INSERT INTO finance\.|INSERT INTO hub\.|INSERT INTO club\.players/);
});

test('scope is granted to runtime functions, never raw minor tables or claim query',()=>{
  assert.match(grants,/GRANT EXECUTE ON FUNCTION identity\.confirm_academy_guardian_otp/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION identity\.has_pending_academy_guardian_proof/);
  assert.doesNotMatch(grants,/GRANT EXECUTE ON FUNCTION identity\.current_academy_guardian_claims/);
  assert.match(grants,/has_table_privilege\(role_name,'identity\.academy_guardian_otp_claims'/);
  assert.match(boundary,/identity\.current_academy_guardian_claims\(text\)/);
  assert.match(fixture,/Invalid OTP HMAC created guardian claim/);
  assert.match(fixture,/Different guardian phone accepted original OTP/);
  assert.match(fixture,/New original Parent cannot see exact authorized child/);
  assert.match(fixture,/Verified Academy original guardian could not grant second parent/);
  assert.match(fixture,/Delegated guardian fabricated an unanchored root/);
  assert.match(fixture,/Original proof revoked but second guardian retained access/);
  assert.match(fixture,/Revoked original guardian retained child scope/);
  assert.match(fixture,/ROLLBACK;/);
});

test('OTP proof expires, is immutable and never reactivates disabled accounts',()=>{
  assert.match(migration,/proof\.reviewed_at\+interval '90 days'/);
  assert.match(migration,/claim\.expires_at>statement_timestamp/);
  assert.match(migration,/blocked\.role='parent' AND NOT blocked\.is_active/);
  assert.match(migration,/blocked\.role IN \('player','coach','management'\)/);
  assert.match(migration,/ON CONFLICT \(registration_id\) DO NOTHING/);
  assert.match(migration,/OTP.*\/\/ never privileges|No imported account digest/);
});
