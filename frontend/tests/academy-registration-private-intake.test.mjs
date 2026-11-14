import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const migration = readFileSync(new URL('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109150000_AcademyRegistrationPrivateIntake.cs',import.meta.url),'utf8');
const publicRoute = readFileSync(new URL('../src/routes/academy/registration/+page.svelte',import.meta.url),'utf8');

test('intake is Academy-only, separate from roster and money',()=>{
  assert.match(migration,/organization_id='lefteria_fc_academy'/);
  assert.match(migration,/status IN \('pending','under_review','needs_information'/);
  assert.match(migration,/onboarded_player_id IS NULL OR status='approved'/);
  assert.doesNotMatch(migration,/INSERT INTO club\.players|INSERT INTO finance\.|CREATE TRIGGER.*charge|pg_cron/i);
});
test('privacy: explicit refusal and sealed sensitive evidence',()=>{
  for(const name of ['participation','medical_authorization','privacy_notice','media','communications','liability','financial'])
    assert.ok(migration.includes("'"+name+"'"),name);
  assert.match(migration,/granted boolean NOT NULL/);
  assert.doesNotMatch(migration,/granted boolean NOT NULL DEFAULT true/);
  assert.match(migration,/evidence_ciphertext bytea NOT NULL/);
  assert.match(migration,/registration_sealed_evidence ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(migration,/allergies_details|signature_data varchar|signature_data text/i);
});
test('new database surfaces are default-deny, with no public intake claim',()=>{
  for(const name of ['registrations','registration_consents','registration_sealed_evidence','registration_review_events'])
    assert.ok(migration.includes('academy_intake.'+name+' ENABLE ROW LEVEL SECURITY'),name);
  assert.match(migration,/REVOKE ALL ON ALL TABLES IN SCHEMA academy_intake FROM PUBLIC/);
  assert.doesNotMatch(migration,/GRANT (?:SELECT|INSERT|UPDATE|DELETE|EXECUTE)|CREATE POLICY/i);
  assert.match(publicRoute, /<fieldset disabled/);
  assert.doesNotMatch(publicRoute, /<form|type="submit"|fetch\(/);
  assert.match(publicRoute, /https:\/\/lefteriafc\.cy\/academy\/registration/);
});
test('review and source imports have structural replay barriers',()=>{
  assert.match(migration,/CREATE UNIQUE INDEX uq_academy_registration_legacy/);
  assert.match(migration,/UNIQUE \(registration_id,status_version\)/);
  assert.match(migration,/decided_by IS NOT NULL/);
  assert.match(migration,/DROP SCHEMA academy_intake/);
});

test('consent revisions preserve changed choices and policy versions',()=>{
  assert.match(migration,/revision integer NOT NULL CHECK/);
  assert.match(migration,/PRIMARY KEY \(registration_id,consent_kind,revision\)/);
  assert.match(migration,/CREATE TRIGGER consent_history_append_only/);
});
test('review audit is enforced at commit and events cannot be rewritten',()=>{
  assert.match(migration,/CREATE TRIGGER review_events_append_only/);
  assert.match(migration,/CREATE CONSTRAINT TRIGGER registration_status_audit/);
  assert.match(migration,/DEFERRABLE INITIALLY DEFERRED/);
  assert.match(migration,/NEW.status_version <> OLD.status_version \+ 1/);
  assert.match(migration,/e.previous_status=OLD.status/);
  assert.match(migration,/e.next_status=NEW.status/);
});

test('consent revisions are consecutive and unchanged statuses need no synthetic audit',()=>{
  assert.match(migration,/CREATE CONSTRAINT TRIGGER consent_revision_sequence/);
  assert.match(migration,/prior\.revision=NEW\.revision-1/);
  assert.match(migration,/OLD\.status IS DISTINCT FROM NEW\.status/);
  assert.match(migration,/OLD\.status_version IS DISTINCT FROM NEW\.status_version/);
});
