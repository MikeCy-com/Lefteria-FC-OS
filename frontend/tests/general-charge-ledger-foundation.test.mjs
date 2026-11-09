import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const file=(name)=>readFileSync(new URL('../../'+name,import.meta.url),'utf8');
const sql=file('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109130000_GeneralChargesPrivateLedger.cs');
const fixture=file('ops/postgres/general-charge-private-ledger-checks.sql');
const workflow=file('.github/workflows/rewrite-build.yml');
const types=['training','event','grassroots','registration','equipment',
  'tournament','transport','other'];

test('original ChargeType enumerates exactly eight types in every source-compatible table',()=>{
  assert.equal(sql.match(/charge_type IN \(/g)?.length,2);
  for(const type of types){
    const matches=sql.match(new RegExp("'"+type+"'","g"))??[];
    assert.ok(matches.length>=2,`Missing original charge type: ${type}`);
  }
  assert.match(sql,/CREATE TABLE finance\.general_charge_templates/);
  assert.match(sql,/CREATE TABLE finance\.general_charge_issues/);
  assert.match(sql,/CREATE TABLE finance\.general_charge_events/);
  assert.match(sql,/period_key varchar\(7\)/);
  assert.match(sql,/legacy_charge_id varchar\(128\)/);
  assert.match(sql,/original_snapshot_digest char\(64\)/);
});

test('amounts use integer cents and status follows settlement evidence, never payment redirects',()=>{
  assert.match(sql,/amount_cents bigint NOT NULL/g);
  assert.match(sql,/currency char\(3\) NOT NULL DEFAULT 'EUR'/);
  assert.match(sql,/CREATE VIEW finance\.general_charge_balances/);
  assert.match(sql,/WHEN coalesce\(a\.paid_cents,0\)=c\.amount_cents THEN 'paid'/);
  assert.match(sql,/timezone\('Asia\/Nicosia',clock_timestamp\(\)\)/);
  assert.match(sql,/CREATE UNIQUE INDEX uq_general_charge_event_reversal/);
  assert.match(sql,/idempotency_id uuid NOT NULL UNIQUE/);
  assert.match(sql,/FOR UPDATE/);
  assert.match(sql,/v_total\+NEW\.amount_cents NOT BETWEEN 0 AND v_amount/);
  assert.match(sql,/financial_events_are_immutable/);
  assert.match(sql,/evidence_type<>'provider_capture'/);
  assert.match(sql,/payment_method IN \('card','online'\)/);
  assert.match(sql,/evidence_type<>'bank_statement'/);
  assert.match(sql,/payment_method='bank'/);
  assert.match(sql,/nullif\(btrim\(evidence_reference\),'\) IS NOT NULL/);
  assert.match(fixture,/provider_capture_as_cash_accepted/);
  assert.match(fixture,/bank_statement_as_card_accepted/);
  assert.match(fixture,/blank_bank_statement_reference_accepted/);
  assert.match(fixture,/valid_bank_card_evidence_not_recorded/);
});

test('charge issuance never automatically generates twelve monthly charges',()=>{
  assert.doesNotMatch(sql,/generate_series\s*\(|CREATE\s+(?:FUNCTION|TRIGGER)\s+finance\.schedule|CALL\s+finance\.repeat_monthly|pg_cron\./i);
  assert.match(sql,/CREATE TABLE finance\.general_charge_parent_verifications/);
  assert.match(sql,/\(link\.charge_id IS NOT NULL\) AS ready_for_parent/);
  assert.match(sql,/verification_digest char\(64\)/);
  assert.match(sql,/FOREIGN KEY \(organization_id,charge_id\)/);
  assert.match(sql,/ALTER TABLE finance\.general_charge_parent_verifications ENABLE ROW LEVEL SECURITY/);
  assert.doesNotMatch(sql,/ready_for_parent boolean NOT NULL DEFAULT true/);
  assert.match(sql,/FOREIGN KEY \(organization_id,player_id\)/);
  assert.match(fixture,/template_created_charges_or_future_months/);
  assert.match(fixture,/unexpected_extra_ledger_events_or_annual_charges/);
});

test('migration creates no new public endpoint or application-role finance grant',()=>{
  assert.match(sql,/REVOKE ALL ON finance\.general_charge_templates/);
  assert.match(sql,/REVOKE ALL ON FUNCTION finance\.guard_general_charge_event/);
  assert.doesNotMatch(sql,/GRANT EXECUTE|GRANT SELECT|GRANT INSERT|GRANT UPDATE/);
  assert.match(fixture,/private_charge_ledger_exposed_to_runtime/);
  assert.match(fixture,/ROLLBACK;/);
  assert.match(fixture,/duplicate_idempotency_collected/);
  assert.match(fixture,/overpayment_was_accepted/);
  assert.match(fixture,/double_reversal_accepted/);
  assert.match(fixture,/issued_invoice_amount_was_mutable/);
  assert.match(fixture,/paid_invoice_cancellation_accepted/);
  assert.match(workflow,/general-charge-private-ledger-checks\.sql/);
});
