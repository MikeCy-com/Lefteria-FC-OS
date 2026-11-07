import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const migration=readFileSync(new URL(
  '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261107220000_MarketingLeadThrottle.cs',
  import.meta.url),'utf8');

test('Academy enquiry throttle stores only a hashed canonical fingerprint',()=>{
  assert.match(migration,/CREATE TABLE marketing\.lead_rate_buckets/);
  assert.match(migration,/fingerprint varchar\(64\) PRIMARY KEY/);
  assert.ok(migration.includes("fingerprint ~ '^[0-9a-f]{64}$'"));
  assert.match(migration,/attempts BETWEEN 1 AND 5/);
  assert.match(migration,/window_started_at timestamptz NOT NULL/);
  assert.match(migration,/last_attempt_at timestamptz NOT NULL/);
  assert.doesNotMatch(migration,/\bremote_ip\b|\bphone\b|\bemail\b/i);
});
test('Academy enquiry throttle remains inaccessible until verified writer',()=>{
  assert.match(migration,/REVOKE ALL ON TABLE marketing\.lead_rate_buckets FROM PUBLIC/);
  assert.match(migration,/ALTER TABLE marketing\.lead_rate_buckets ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(migration,/^\s*(?:GRANT|INSERT INTO|CREATE POLICY)\b|\bSECURITY DEFINER\b/im);
});
