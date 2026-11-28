import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const page = read('../src/routes/admin/academy-office/+page.svelte');
const component = read('../src/lib/admin/AcademyGuardianProofReview.svelte');
const endpoint = read('../../backend/src/LefteriaFc.Api/Crm/AcademyGuardianProofEndpoints.cs');
const program = read('../../backend/src/LefteriaFc.Api/Program.cs');
const migration = read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115960000_AcademyGuardianProofReview.cs');
const fixture = read('../../ops/postgres/academy-enrollment-checks.sql');
const grants = read('../../ops/postgres/web-admin-auth-grants.sql');
const boundary = read('../../ops/postgres/web-admin-auth-permissions-check.sql');
const edge = read('../../deploy/preview/generate-edge.mjs');
const edgeTest = read('../../deploy/preview/reviewed-admin-operations.test.mjs');

test('verified guardian review is inside the approved Academy registration, not another menu', () => {
  assert.doesNotThrow(() => compile(page, {filename:'academy-office/+page.svelte',generate:'server'}));
  assert.doesNotThrow(() => compile(component,{filename:'AcademyGuardianProofReview.svelte',generate:'server'}));
  assert.match(page,/row\.status === 'approved'/);
  assert.match(page,/row\.onboarded_player_id && row\.submission_source === 'website'/);
  assert.match(page,/\$auth\.session\.user\.role === 'super_admin'/);
  assert.match(page,/<AcademyGuardianProofReview/);
  assert.match(component,/data-testid="academy-guardian-proof-review"/);
  assert.match(component,/app_access_enabled !== false/);
  assert.match(component,/guardianPhone\.replace/);
  assert.match(component,/attested = \$state\(false\)/);
  assert.match(component,/bind:checked=\{attested\}/);
  assert.match(component,/endpoint\+'\/verify'/);
  assert.match(component,/endpoint\+'\/revoke'/);
});

test('both HTTP and SQL are separately super-admin locked and fail closed', () => {
  assert.match(program,/app\.MapAcademyGuardianProof\(\)/);
  assert.match(endpoint,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(endpoint,/session\.User\.Role != "super_admin"/);
  assert.match(endpoint,/AdminPermissions\.Has\(session\.User\.Role, "academy:write"\)/);
  assert.match(endpoint,/context\.Response\.Headers\.CacheControl = "private, no-store"/);
  assert.match(endpoint,/\.ExcludeFromDescription\(\)/);
  assert.match(endpoint,/identity\.admin_academy_guardian_proof/);
  assert.match(migration,/club\.authorize_admin_football/);
  assert.match(migration,/v_role<>'super_admin'/);
  assert.match(migration,/v_approval\.recorded_by IS NOT NULL/);
  assert.match(migration,/v_approval\.onboarded_player_id IS NULL/);
  assert.match(migration,/academy_intake\.registration_placements/);
  assert.match(migration,/guardian_registration_not_onboarded/);
});

test('proof ledger is non-entitling, auditable, immutable and revocable', () => {
  for (const marker of [
    'CREATE TABLE academy_intake.guardian_proof_reviews',
    'CREATE TABLE academy_intake.guardian_proof_review_events',
    'ENABLE ROW LEVEL SECURITY', 'REVOKE ALL',
    'guardian_proof_immutable', 'guardian_proof_event_immutable',
    'registration_consents', 'registration_sealed_evidence',
    'v_phone<>regexp_replace(v_approval.guardian_phone',
    'case_reference', 'attested',
    "'status','verified_proof'", "'status','revoked'",
    "'app_access_enabled',false",
    'audit.security_events', 'reviewed_by', 'revoked_by',
    'guardian_proof_history_requires_forward_recovery'
  ]) assert.ok(migration.includes(marker),marker);
  assert.doesNotMatch(migration,/INSERT INTO identity\.mobile_identities|INSERT INTO identity\.mobile_record_links|INSERT INTO identity\.family_guardian_permissions/);
  assert.doesNotMatch(endpoint,/identity\.current_phone_granted_children|identity\.has_current_guardian_phone_grant/);
});

test('reviewed DB and preview boundaries enumerate only the exact restricted function and paths', () => {
  assert.match(grants,/GRANT EXECUTE ON FUNCTION identity\.admin_academy_guardian_proof\(text,text,jsonb\) TO :"admin_auth_role"/);
  assert.match(grants,/'identity\.admin_academy_guardian_proof\(text,text,jsonb\)'::regprocedure/);
  assert.match(boundary,/'identity\.admin_academy_guardian_proof\(text,text,jsonb\)'::regprocedure/);
  for(const route of [
    "['get','/api/admin/academy/office/guardians']",
    "['post','/api/admin/academy/office/guardians/verify']",
    "['post','/api/admin/academy/office/guardians/revoke']"
  ]) assert.ok(edge.includes(route),route);
  assert.match(edgeTest,/reviewedHiddenAdminEntries\.length,26/);
  assert.match(fixture,/Location manager could attest guardian identity/);
  assert.match(fixture,/Proof review exposed guardian data or app rights/);
  assert.match(fixture,/Guardian proof revoke failed/);
  assert.match(fixture,/ROLLBACK;/);
});
