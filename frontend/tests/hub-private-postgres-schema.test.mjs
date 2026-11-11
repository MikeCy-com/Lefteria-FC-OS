import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const migration=readFileSync(new URL('../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110180000_HubPrivateFoundation.cs',import.meta.url),'utf8');

test('Hub schema captures content revisions AI media approval evidence and subscriptions',()=>{
  for(const table of ['content','revisions','media_assets','review_events','fan_subscriptions'])
    assert.match(migration,new RegExp('CREATE TABLE hub\\.'+table+' \\('));
  assert.match(migration,/sha256 varchar\(64\) NOT NULL/);
  assert.match(migration,/generated_by_ai boolean NOT NULL/);
  assert.match(migration,/text_review varchar\(12\) NOT NULL/);
  assert.match(migration,/visual_review varchar\(12\) NOT NULL/);
  assert.match(migration,/approved_by uuid/);
  assert.match(migration,/provider_reference varchar\(180\)/);
  assert.match(migration,/verified_at timestamptz/);
  assert.match(migration,/expires_at timestamptz/);
  assert.match(migration,/consent_at timestamptz NOT NULL/);
});

test('Hub defaults to private RLS without unintended grants or auto-publication',()=>{
  assert.match(migration,/REVOKE ALL ON SCHEMA hub FROM PUBLIC/);
  assert.match(migration,/REVOKE ALL ON ALL TABLES IN SCHEMA hub FROM PUBLIC/);
  for(const table of ['content','revisions','media_assets','review_events','fan_subscriptions'])
    assert.match(migration,new RegExp('ALTER TABLE hub\\.'+table+' ENABLE ROW LEVEL SECURITY'));
  assert.doesNotMatch(migration,/GRANT (SELECT|INSERT|UPDATE|DELETE|ALL)/i);
  assert.doesNotMatch(migration,/CREATE POLICY|SECURITY DEFINER|CREATE FUNCTION|MapGet\(|MapPost\(/);
  assert.match(migration,/status varchar\(20\) NOT NULL DEFAULT 'ai_draft'/);
  assert.match(migration,/status varchar\(16\) NOT NULL DEFAULT 'pending'/);
  assert.match(migration,/status<>'active' OR/);
});

test('Hub schema enforces organization age audience and integrity constraints',()=>{
  for(const check of ['ck_hub_content_scope','ck_hub_content_audiences',
    'ck_hub_content_ages','ck_hub_content_approval','ck_hub_content_publication',
    'ck_hub_sub_verified','ux_hub_subscription_subscriber_owner_active'])
    assert.ok(migration.includes(check),check);
  assert.match(migration,/REFERENCES identity\.mobile_identities\(id\)/);
  assert.match(migration,/REFERENCES club\.organizations\(id\)/);
});