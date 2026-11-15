import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const read = path => readFileSync(new URL(path, import.meta.url), 'utf8');
const page = read('../src/routes/admin/academy-office/+page.svelte');
const endpoint = read('../../backend/src/LefteriaFc.Api/Crm/AcademyOfficeEndpoints.cs');
const migration = read('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115920000_AcademyRegistrationAtomicEnrollment.cs');
const grants = read('../../ops/postgres/web-admin-auth-grants.sql');
const permissions = read('../../ops/postgres/web-admin-auth-permissions-check.sql');
const sql = read('../../ops/postgres/academy-enrollment-checks.sql');
const ci = read('../../.github/workflows/rewrite-build.yml');
const workspace = read('../../scripts/workspace/run-integration-tests.sh');

test('CRM has one compiled application inbox and a verified city/location/group enrollment flow', () => {
  assert.doesNotThrow(() => compile(page, {
    filename:'academy-office/+page.svelte', generate:'server'
  }));
  assert.match(page, /data-testid="academy-registration-unified-inbox"/);
  assert.match(page, /data-testid="crm-approve-enroll-form"/);
  assert.match(page, /row\.submission_source !== 'website'/);
  assert.match(page, /permittedCities\.some/);
  assert.match(page, /permittedLocations\.some/);
  assert.match(page, /permittedGroups\.some/);
  assert.match(page, /organization_id=lefteria_fc_academy/);
  assert.match(page, /registrations\/enroll/);
  assert.match(page, /status === 'approved'/);
  assert.doesNotMatch(page, /position:\s*'Midfielder'|number:\s*0[^-]/);
});

test('HTTP API never equates status-only approval with automatic roster enrollment', () => {
  assert.match(endpoint, /MapPost\("\/registrations\/enroll"/);
  assert.match(endpoint, /identity\.enroll_admin_academy_registration/);
  assert.match(endpoint, /body\.CityId == Guid\.Empty/);
  assert.match(endpoint, /body\.LocationId == Guid\.Empty/);
  assert.match(endpoint, /body\.RequestId == Guid\.Empty/);
  assert.match(endpoint, /body\.Position is not/);
  assert.match(endpoint, /context\.Response\.Headers\.CacheControl = "private, no-store"/);
  assert.match(endpoint, /AdminPermissions\.Has\(session\.User\.Role, write \? "academy:write" : "academy:read"\)/);
});

test('SQL transaction is consent-gated, exact-location authorized, audited and idempotent', () => {
  for (const v of [
    'club.authorize_admin_football', 'club.admin_football_allowed',
    'club.academy_group_location_assignments', 'club.football_locations',
    'club.roster_import_state', 'registration_sealed_evidence',
    'registration_consents', 'registration_placements', 'club.players',
    'club.player_academy_groups', 'registration_review_events',
    'pg_advisory_xact_lock', 'onboarded_player_id',
    'security_events', 'CREATE FUNCTION identity.enroll_admin_academy_registration',
    'SECURITY DEFINER', 'REVOKE ALL ON FUNCTION identity.enroll_admin_academy_registration'
  ]) assert.ok(migration.includes(v),v);
  assert.match(migration,/v_application\.recorded_by IS NOT NULL/);
  assert.match(migration,/status<>'under_review'/);
  assert.match(migration,/v_application\.status_version<>v_expected/);
  assert.match(migration,/v_application\.onboarded_player_id IS NOT NULL/);
  assert.match(migration,/v_position NOT IN/);
  assert.match(migration,/v_number NOT BETWEEN 1 AND 99/);
  assert.doesNotMatch(migration,/INSERT INTO identity\.mobile_identities|INSERT INTO finance\./);
});

test('dedicated DB privileges and rollback-only PostgreSQL acceptance tests', () => {
  assert.match(grants, /GRANT EXECUTE ON FUNCTION identity\.enroll_admin_academy_registration\(text,jsonb\) TO :"admin_auth_role"/);
  assert.match(permissions, /'identity\.enroll_admin_academy_registration\(text,jsonb\)'::regprocedure/);
  assert.match(sql, /BEGIN;/);
  assert.match(sql, /ROLLBACK;/);
  assert.match(sql, /Manager enrolled unassigned public registration/);
  assert.match(sql, /Foreign location accepted/);
  assert.match(sql, /Enrollment replay was not idempotent/);
  assert.match(sql, /Revoked administrator retained enrollment/);
  assert.match(ci, /academy-enrollment-checks\.sql/);
  assert.match(workspace, /academy-enrollment-checks\.sql/);
});
