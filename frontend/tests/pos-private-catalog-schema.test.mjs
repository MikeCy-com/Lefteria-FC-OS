import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const migration = readFileSync(new URL(
  '../../backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108100000_PosPrivateCatalog.cs',
  import.meta.url), 'utf8');
const program = readFileSync(new URL(
  '../../backend/src/LefteriaFc.Api/Program.cs', import.meta.url), 'utf8');
const catalog = readFileSync(new URL(
  '../src/lib/admin/module-catalog.ts', import.meta.url), 'utf8');

test('POS foundation keeps cents, scoped categories and variants, no sales or payment endpoints', () => {
  for (const expected of [
    'CREATE SCHEMA IF NOT EXISTS pos',
    'CREATE TABLE pos.categories',
    'CREATE TABLE pos.products',
    'CREATE TABLE pos.product_variants',
    'FOREIGN KEY (organization_id,category_id)',
    'FOREIGN KEY (organization_id,product_id)',
    'price_cents integer NOT NULL',
    'cost_cents integer NOT NULL',
    'price_cents BETWEEN 1 AND 10000000',
    'cost_cents BETWEEN 0 AND 10000000',
    'ALTER TABLE pos.categories ENABLE ROW LEVEL SECURITY',
    'ALTER TABLE pos.products ENABLE ROW LEVEL SECURITY',
    'ALTER TABLE pos.product_variants ENABLE ROW LEVEL SECURITY',
    "pos_catalog_data_requires_forward_recovery"
  ]) assert.ok(migration.includes(expected),expected);
  assert.doesNotMatch(migration,/^\s*GRANT\s+|\bEXECUTE\b|\bcheckout\(|\bCREATE TABLE pos\.sales\b/im);
  assert.doesNotMatch(program,/MapAdminPos\(|MapPosSales\(|MapPosCheckout\(/);
});

test('POS read-only catalog is live but registers and sales remain blocked', () => {
  assert.match(catalog,/id: 'pos', name: 'POS & κατάστημα'/);
  for (const id of ['registers', 'orders']) {
    assert.match(catalog, new RegExp("id: '"+id+"'[^\\n]*status: 'planned'"));
  }
});
