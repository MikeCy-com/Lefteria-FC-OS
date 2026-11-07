import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const migration=readFileSync(new URL('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261107215000_MarketingLeadInbox.cs',import.meta.url),'utf8');

test('marketing lead inbox requires exact organization, consent and source attribution',()=>{
  assert.match(migration,/REFERENCES club\.organizations\(id\) ON DELETE RESTRICT/);
  assert.match(migration,/organization_id IN \('lefteria_fc','lefteria_fc_academy'\)/);
  assert.match(migration,/consent_recorded_at timestamptz NOT NULL/);
  assert.match(migration,/consent_gdpr IS TRUE/);
  assert.match(migration,/consent_marketing boolean NOT NULL DEFAULT false/);
  assert.match(migration,/partner_slug varchar\(64\)/);
  assert.match(migration,/source='partner_referral'/);
  assert.match(migration,/phone IS NOT NULL OR email IS NOT NULL/);
  assert.match(migration,/player_age IS NULL OR player_age BETWEEN 4 AND 99/);
});

test('schema-only marketing inbox has no automatic data access or provider effects',()=>{
  assert.match(migration,/REVOKE ALL ON SCHEMA marketing FROM PUBLIC/);
  assert.match(migration,/REVOKE ALL ON TABLE marketing\.public_leads FROM PUBLIC/);
  assert.match(migration,/ALTER TABLE marketing\.public_leads ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(migration,/\bGRANT\b|\bINSERT INTO\b|\bSECURITY DEFINER\b|\bCREATE POLICY\b|\bSMS\b/i);
});
