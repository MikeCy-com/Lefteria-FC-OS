import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=(p)=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const schema=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110160000_PosPrivateLocationLedger.cs');
const guards=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110170000_PosLedgerIntegrityGuards.cs');
test('POS cities, locations, stock and operators are private and organization-scoped',()=>{
  for(const name of ['cities','locations','location_products','location_stock','operators','operator_sessions',
    'sales','sale_lines','sale_payment_events','stock_movements']){
    assert.match(schema,new RegExp('CREATE TABLE pos\\.'+name+' \\('));
    assert.match(schema,new RegExp('ALTER TABLE pos\\.'+name+' ENABLE ROW LEVEL SECURITY'));
  }
  assert.match(schema,/UNIQUE\(organization_id,code\)/);
  assert.match(schema,/UNIQUE\(organization_id,id,location_id\)/);
  assert.match(schema,/FOREIGN KEY\(organization_id,operator_id,location_id\)/);
  assert.match(schema,/FOREIGN KEY\(sale_id,organization_id,location_id\)/);
  assert.match(schema,/FOREIGN KEY\(organization_id,location_id,product_id\)/);
  assert.match(schema,/REFERENCES pos\.product_variants\(organization_id,product_id,id\)/);
});
test('sale snapshots are immutable EUR cents and per-location receipts unique',()=>{
  assert.match(schema,/currency char\(3\) NOT NULL DEFAULT 'EUR'/);
  assert.match(schema,/total_cents bigint NOT NULL CHECK/);
  assert.match(schema,/quantity::bigint \* unit_price_cents::bigint/);
  assert.match(schema,/UNIQUE\(organization_id,location_id,client_sale_id\)/);
  assert.match(schema,/UNIQUE\(organization_id,location_id,business_date,receipt_number\)/);
  for(const name of ['pos_sale_immutable','pos_sale_lines_immutable','pos_payment_events_immutable','pos_stock_movement_immutable'])
    assert.ok(schema.includes(name),name);
  assert.match(guards,/pos_sale_total_mismatch/);
  assert.match(guards,/DEFERRABLE INITIALLY DEFERRED/);
});
test('payments require evidence and cannot exceed captured/paid amounts',()=>{
  assert.match(schema,/event_type='cash_received' AND cash_tendered_cents IS NOT NULL/);
  assert.match(schema,/event_type='card_capture' AND cash_tendered_cents IS NULL/);
  assert.match(schema,/evidence_reference IS NOT NULL/);
  assert.match(schema,/event_request_id uuid NOT NULL UNIQUE/);
  assert.match(schema,/reversed_event_id uuid UNIQUE/);
  assert.match(guards,/FROM pos\.sales\s+WHERE id=NEW\.sale_id FOR UPDATE/);
  assert.match(guards,/v_captured\+NEW\.amount_cents>v_sale_total/);
  assert.match(guards,/v_refunded\+NEW\.amount_cents>v_captured/);
  assert.match(guards,/v_original\.event_type<>'card_capture'/);
});
test('inventory is serialized and mutations require immutable evidence',()=>{
  assert.match(guards,/FROM pos\.location_stock[\s\S]*?FOR UPDATE/);
  assert.match(guards,/v_on_hand<>NEW\.before_quantity/);
  assert.match(guards,/SET on_hand=NEW\.after_quantity,version=version\+1/);
  assert.match(schema,/CHECK \(\(reason IN \('sale','return'\)\)=\(sale_id IS NOT NULL\)\)/);
  assert.match(schema,/REVOKE ALL ON ALL TABLES IN SCHEMA pos FROM PUBLIC/);
  assert.doesNotMatch(schema,/GRANT SELECT|GRANT INSERT|GRANT EXECUTE/);
});
