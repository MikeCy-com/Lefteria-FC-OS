import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const migration=readFileSync(new URL(
  '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261107223000_MarketingLeadAtomicWrite.cs',
  import.meta.url),'utf8');

test('private Academy writer is a single bounded throttle + lead + audit operation',()=>{
  for(const source of [
    "CREATE FUNCTION marketing.store_academy_lead(",
    "SECURITY DEFINER",
    "SET search_path=pg_catalog,pg_temp",
    "ON CONFLICT(fingerprint) DO UPDATE",
    "marketing.lead_rate_buckets.attempts<5",
    "RETURN 'rate_limited'",
    "INSERT INTO marketing.public_leads(",
    "INSERT INTO marketing.lead_events(",
    "RETURN 'stored'",
    "VALUES (\n                _lead_id,'lefteria_fc_academy'",
    "consent_recorded_at"
  ])assert.ok(migration.includes(source),source);
});
test('marketing writer has no application grant, HTTP route or live provider',()=>{
  assert.ok(migration.includes("REVOKE ALL ON FUNCTION marketing.store_academy_lead("));
  assert.ok(migration.includes("FROM PUBLIC;"));
  assert.ok(migration.includes("ALTER TABLE marketing.lead_events ENABLE ROW LEVEL SECURITY"));
  assert.doesNotMatch(migration,/^\s*GRANT\s+EXECUTE|^\s*CREATE POLICY|^\s*INSERT INTO\s+identity\./im);
  assert.doesNotMatch(migration,/\bTwilio\b|\bHttpClient\b|\bhostinger\b/i);
});

test('versioned concurrent throttle fix preserves private role and monotonic timestamp',()=>{
  const upgrade=readFileSync(new URL(
    '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108080000_MarketingLeadMonotonicThrottle.cs',
    import.meta.url),'utf8');
  assert.match(upgrade,/CREATE OR REPLACE FUNCTION marketing\.store_academy_lead/);
  assert.ok(upgrade.includes('GREATEST(marketing.lead_rate_buckets.last_attempt_at,v_time)'));
  assert.ok(upgrade.includes('migrationBuilder.Sql(OriginalFunction);'), 'rollback must restore prior function');
  assert.ok(upgrade.includes('migrationBuilder.Sql(Guard)'));
  assert.ok(upgrade.includes('migrationBuilder.Sql(DenyPublicExecution)'));
  assert.doesNotMatch(upgrade,/^\s*GRANT\s+/im);
  const original=readFileSync(new URL(
    '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261107223000_MarketingLeadAtomicWrite.cs',
    import.meta.url),'utf8');
  assert.ok(original.includes('last_attempt_at=v_time'), 'historical migration must be unchanged');
});
