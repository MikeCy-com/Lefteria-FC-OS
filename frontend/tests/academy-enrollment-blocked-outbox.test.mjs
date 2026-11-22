import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261122160000_AcademyPlacementSafeWorkflowOutbox.cs');
const check=read('ops/postgres/academy-enrollment-checks.sql');

test('one blocked welcome reservation follows actual Academy placement transaction',()=>{
  assert.match(migration,/AFTER INSERT ON academy_intake\.registration_placements/);
  assert.match(migration,/PRIMARY KEY \(registration_id,stage\)/);
  assert.match(migration,/stage varchar\(32\) NOT NULL CHECK \(stage='welcome'\)/);
  assert.match(migration,/delivery_state varchar\(32\) NOT NULL DEFAULT 'blocked_verification'/);
  assert.match(migration,/CHECK \(delivery_state='blocked_verification'\)/);
  assert.match(migration,/CHECK \(NOT consent_verified\)/);
  assert.match(migration,/CHECK \(NOT provider_enabled\)/);
  assert.match(migration,/ON CONFLICT \(registration_id,stage\) DO NOTHING/);
  assert.match(migration,/REVOKE ALL ON academy_intake\.placement_workflow_outbox FROM PUBLIC/);
  assert.match(migration,/ENABLE ROW LEVEL SECURITY/);
  assert.match(migration,/REVOKE ALL ON FUNCTION academy_intake\.reserve_placement_welcome\(\) FROM PUBLIC/);
});

test('reservation does not disclose contact, health card, documents or send/charge',()=>{
  assert.doesNotMatch(migration,/guardian_phone|guardian_email|allergies_details|medical_details|koa_health_card_status|health_card_number|viber_token|acs_address/i);
  assert.doesNotMatch(migration,/INSERT INTO finance\.|INSERT INTO communications\.|send_viber|payabl/i);
  assert.doesNotMatch(migration,/GRANT EXECUTE|GRANT (?:SELECT|INSERT|UPDATE|DELETE)/);
});

test('disposable PostgreSQL acceptance checks denied writes and idempotent welcome',()=>{
  assert.match(check,/Denied enrollment reserved an outbound event/);
  assert.match(check,/Approved placement did not reserve exactly one blocked welcome event/);
  assert.match(check,/Replay duplicated the post-enrollment welcome event/);
});
