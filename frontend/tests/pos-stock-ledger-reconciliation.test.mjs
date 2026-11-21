import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115955000_PosSiteStockReconciliationRead.cs');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosSiteEndpoints.cs');
const ui=read('frontend/src/lib/admin/AdminPosStockReconciliationPanel.svelte');
const host=read('frontend/src/lib/admin/AdminPosSitesPanel.svelte');
const grants=read('ops/postgres/pos-academy-site-admin-grants.sql');
const preview=read('deploy/preview/generate-edge.mjs');
const fixture=read('ops/postgres/pos-site-isolation-checks.sql');
const guards=read('ops/postgres/pos-cashier-ledger-checks.sql');

test('read-only reconciliation isolates owner and canonical city/location, without changing inventory',()=>{
  assert.match(migration,/CREATE FUNCTION pos_cashier.read_admin_site_stock_reconciliation/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/pos_cashier.authorize_site_finances\(_digest,_owner,_location\)/);
  assert.match(migration,/FROM pos.location_stock/);
  assert.match(migration,/FROM pos_cashier.stock/);
  assert.match(migration,/FULL OUTER JOIN/);
  assert.match(migration,/legacy.on_hand-cashier.on_hand/);
  assert.match(migration,/pos.location_products lp/);
  assert.match(migration,/legacy_missing/);
  assert.match(migration,/cashier_missing/);
  assert.match(migration,/not_assigned/);
  assert.match(migration,/quantity_mismatch/);
  assert.match(migration,/v_total>150/);
  assert.match(migration,/'physical_count_approved',false/);
  assert.match(migration,/'stock_ready_for_trading',false/);
  assert.match(migration,/REVOKE ALL ON FUNCTION pos_cashier.read_admin_site_stock_reconciliation/);
  assert.doesNotMatch(migration,/\b(?:INSERT INTO|UPDATE pos|DELETE FROM|TRUNCATE|GRANT EXECUTE)\b/i);
  assert.match(guards,/pos_site_stock_reconciliation_privacy_guard_missing/);
});
test('manager UI distinguishes matching electronic balances from physical counts',()=>{
  assert.match(api,/MapGet\("\/\{locationId\}\/stock-reconciliation"/);
  assert.match(api,/context, false, false, true, ct/);
  assert.match(api,/pos_cashier.read_admin_site_stock_reconciliation/);
  assert.match(api,/ExcludeFromDescription\(\)/);
  assert.match(host,/AdminPosStockReconciliationPanel/);
  assert.match(host,/locationId=\{selectedSite.pos_location_id\}/);
  assert.match(ui,/data-testid="pos-site-stock-reconciliation"/);
  assert.match(ui,/r.physical_count_approved!==false/);
  assert.match(ui,/r.stock_ready_for_trading!==false/);
  assert.match(ui,/r.total_skus!==lines.length/);
  assert.match(ui,/ΜΗ ΕΓΚΕΚΡΙΜΕΝΗ/);
  assert.match(ui,/φυσική απογραφή/);
  assert.doesNotMatch(ui,/localStorage|sessionStorage|\{@html|method:'POST'|method:'PUT'/);
});
test('staging grant opens only private scoped read, not sale or financial write',()=>{
  assert.match(grants,/MANUAL STAGING REVIEW ONLY/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION pos_cashier.read_admin_site_stock_reconciliation/);
  assert.match(grants,/pos_site_raw_financial_table_access_denied/);
  assert.doesNotMatch(grants,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON pos_cashier/);
  assert.match(preview,/\['get','\/api\/admin\/pos\/sites\/\{locationId\}\/stock-reconciliation'\]/);
  assert.doesNotMatch(preview,/\['post','\/api\/admin\/pos\/sites\/\{locationId\}\/stock-reconciliation'\]/);
});
test('database acceptance exposes intentional drift after a completed cash sale and rejects other sites',()=>{
  assert.match(fixture,/pos_stock_missing_legacy_ledger_not_reported/);
  assert.match(fixture,/pos_stock_balances_match_wrong_or_approved/);
  assert.match(fixture,/pos_completed_sale_stock_ledger_drift_hidden/);
  assert.match(fixture,/pos_manager_read_other_location_inventory/);
  assert.match(fixture,/pos_other_city_manager_read_inventory/);
  assert.match(fixture,/pos_cashier_read_site_inventory_reconciliation/);
  assert.match(fixture,/synthetic_sale_or_policy_leaked_past_rollback/);
  assert.doesNotMatch(fixture,/DELETE FROM pos_cashier\.sales/);
});
