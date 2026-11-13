import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const sql=readFileSync(new URL('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113180000_HubScheduledDeliveryFoundation.cs',import.meta.url),'utf8');

test('every identity and audience has its own enrolment clock and weekly position',()=>{
  assert.match(sql,/CREATE TABLE hub\.learning_enrollments/);
  assert.match(sql,/identity_id uuid NOT NULL/);
  assert.match(sql,/started_at timestamptz NOT NULL/);
  assert.match(sql,/next_due_at timestamptz NOT NULL/);
  assert.match(sql,/delivery_week integer NOT NULL DEFAULT 1/);
  assert.match(sql,/UNIQUE \(identity_id,organization_id,audience\)/);
  assert.match(sql,/audience IN \('parent','player','coach','fan_parent','fan_player'\)/);
});

test('weekly delivery is idempotent and cannot silently send duplicates',()=>{
  assert.match(sql,/CREATE TABLE hub\.learning_delivery_outbox/);
  assert.match(sql,/UNIQUE \(enrollment_id,delivery_week,channel\)/);
  assert.match(sql,/UNIQUE \(enrollment_id,content_id,channel\)/);
  assert.match(sql,/revision_id uuid NOT NULL/);
  assert.match(sql,/channel='in_app'/);
  assert.match(sql,/state IN \('pending','delivered','cancelled'\)/);
});

test('fan access requires verified unexpired active Payabl subscription and live fan role',()=>{
  assert.match(sql,/CREATE FUNCTION hub\.fan_delivery_entitled/);
  assert.match(sql,/i\.is_active AND i\.role='fan'/);
  assert.match(sql,/s\.provider='payabl'/);
  assert.match(sql,/s\.status='active'/);
  assert.match(sql,/s\.verified_at IS NOT NULL AND s\.verified_at<=_at/);
  assert.match(sql,/s\.expires_at>_at AND s\.cancelled_at IS NULL/);
  assert.match(sql,/s\.audience_parent/);
  assert.match(sql,/s\.audience_player/);
});

test('no worker or mobile role is granted access before audited delivery implementation',()=>{
  assert.match(sql,/REVOKE ALL ON TABLE hub\.learning_enrollments FROM PUBLIC/);
  assert.match(sql,/REVOKE ALL ON TABLE hub\.learning_delivery_outbox FROM PUBLIC/);
  assert.match(sql,/REVOKE ALL ON FUNCTION hub\.fan_delivery_entitled\(uuid,text,text,timestamptz\) FROM PUBLIC/);
  assert.match(sql,/FORCE ROW LEVEL SECURITY/g);
  assert.doesNotMatch(sql,/GRANT EXECUTE|GRANT SELECT|GRANT INSERT/);
  assert.match(sql,/Refusing to delete enrolled members or delivery history/);
});
