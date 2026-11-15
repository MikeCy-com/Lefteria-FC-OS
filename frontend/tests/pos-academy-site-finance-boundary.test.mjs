import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=(p)=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const boundary=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115910000_PosAcademySiteFinancialBoundary.cs');
const terminals=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115911000_PosTerminalProvisioning.cs');
const history=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115912000_PosSiteSalesHistory.cs');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosSiteEndpoints.cs');
const locations=read('backend/src/LefteriaFc.Api/Pos/AdminPosLocationEndpoints.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const ui=read('frontend/src/lib/admin/AdminPosSitesPanel.svelte');
const pos=read('frontend/src/routes/admin/pos/+page.svelte');
const grants=read('ops/postgres/pos-academy-site-admin-grants.sql');

test('Academy POS locations map to canonical city and location, not another independent geography',()=>{
  assert.match(boundary,/CREATE TABLE pos\.academy_football_location_links/);
  assert.match(boundary,/UNIQUE\(academy_location_id\)/);
  assert.match(boundary,/REFERENCES club\.football_locations\(city_id,id\)/);
  assert.match(boundary,/JOIN club\.football_cities c ON c\.id=l\.city_id/);
  assert.match(boundary,/ensure_admin_academy_pos_site/);
  assert.match(boundary,/IF v_role<>'super_admin'/);
  assert.match(locations,/pos_academy_canonical_site_required/);
  assert.match(ui,/Οι πόλεις και τα σημεία δημιουργούνται στην Ακαδημία/);
});

test('manager POS site finances require exact active city/location grants at SQL boundary',()=>{
  assert.match(boundary,/CREATE FUNCTION pos_cashier\.authorize_site_finances/);
  assert.match(boundary,/link\.academy_location_id,\s*s\.id/);
  assert.match(boundary,/club\.admin_football_allowed\(\s*v_actor,v_role,_owner,v_scope,v_academy_location,false\)/);
  assert.match(boundary,/v_role<>'super_admin'/);
  assert.match(boundary,/pos_all_locations_super_admin_only/);
  assert.match(boundary,/pos_global_finance_super_admin_only/);
  assert.match(boundary,/CREATE OR REPLACE FUNCTION pos_cashier\.current_cashier_authority/);
  assert.match(boundary,/cashier_site_grant_revoked/);
  assert.match(boundary,/CREATE OR REPLACE FUNCTION pos_cashier\.read_admin_cashier_registers/);
  assert.match(boundary,/pos_site_security_requires_reviewed_forward_recovery/);
});

test('per-site summaries count verified refunds and receipted sales only for this register location',()=>{
  assert.match(boundary,/CREATE FUNCTION pos_cashier\.read_admin_pos_site_finances/);
  assert.match(boundary,/v_actor:=pos_cashier\.authorize_site_finances\(_digest,_owner,_location\)/);
  assert.match(boundary,/r\.location_id=_location/);
  assert.match(boundary,/FROM pos_cashier\.receipts rc/);
  assert.match(boundary,/f\.status='verified'/);
  assert.match(boundary,/p\.status='verified' AND s\.status='paid'/);
  assert.match(boundary,/'finance_posting_active',false/);
  assert.doesNotMatch(boundary,/UPDATE pos_cashier\.sales SET status='paid'/);
});

test('super admin provisions terminals with distinct existing accounts and exact Academy scope',()=>{
  assert.match(terminals,/CREATE FUNCTION pos_cashier\.create_admin_terminal/);
  assert.match(terminals,/CREATE FUNCTION pos_cashier\.set_admin_terminal_operator/);
  assert.match(terminals,/pos_operator_location_grant_required/);
  assert.match(terminals,/pos_operator_open_shift_must_close/);
  assert.match(terminals,/club\.admin_football_allowed\(/);
  assert.match(terminals,/pos_cashier\.cashier_assignments/);
  assert.match(terminals,/v_subject_role NOT IN \('manager','super_admin'\)/);
  assert.match(terminals,/REVOKE ALL ON FUNCTION pos_cashier\.set_admin_terminal_operator/);
  assert.doesNotMatch(terminals,/INSERT INTO pos_cashier\.(sales|payment_events|receipts|refund_events)/);
});

test('bounded transaction history is read-only and never infers paid from a pending order',()=>{
  assert.match(history,/CREATE FUNCTION pos_cashier\.read_admin_pos_site_sales/);
  assert.match(history,/pos_cashier\.authorize_site_finances\(_digest,_owner,_location\)/);
  assert.match(history,/r\.location_id=_location/);
  assert.match(history,/LIMIT 50/);
  assert.match(history,/LEFT JOIN pos_cashier\.receipts/);
  assert.doesNotMatch(history,/INSERT INTO|UPDATE pos_cashier\.sales|DELETE FROM/);
  assert.match(ui,/Η εμφάνιση εκκρεμούς συναλλαγής δεν σημαίνει επιβεβαιωμένη πληρωμή/);
});

test('HTTP and UI require present admin session, exact organization and finance role',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/context\.Request\.Query\["organization_id"\]\.Count!=1/);
  assert.match(api,/session\.User\.Organizations\.Any\(o=>o\.Id==organization\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role,"finance:read"\)/);
  assert.match(api,/session\.User\.Role!="super_admin"/);
  assert.match(api,/pos_cashier\.read_admin_pos_site_finances/);
  assert.match(api,/pos_cashier\.read_admin_pos_site_sales/);
  assert.match(api,/pos_cashier\.ensure_admin_academy_pos_site/);
  assert.match(program,/app\.MapAdminPosSites\(\)/);
  assert.match(pos,/AdminPosSitesPanel/);
  assert.match(pos,/canProvision=\{\$auth\.session\.user\.role==='super_admin'\}/);
  assert.match(pos,/\$auth\.session\.user\.role==='super_admin'/);
  assert.doesNotMatch(api,/payabl\.capture|refund\.execute|checkout\.settle/i);
});

test('operator-approved grant script keeps raw financial tables and private helpers inaccessible',()=>{
  assert.match(grants,/MANUAL STAGING REVIEW ONLY/);
  assert.match(grants,/pos_site_private_authorizer_exposed/);
  assert.match(grants,/pos_site_raw_financial_table_access_denied/);
  assert.match(grants,/pos_site_canonical_link_exposed/);
  assert.match(grants,/has_function_privilege\(m,f,'EXECUTE'\)/);
  assert.doesNotMatch(grants,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON pos_cashier\./);
});


test('real PostgreSQL POS site isolation is executed in CI and workspace staging suite',()=>{
  const sql=read('ops/postgres/pos-site-isolation-checks.sql');
  const ci=read('.github/workflows/rewrite-build.yml');
  const integration=read('scripts/workspace/run-integration-tests.sh');
  assert.match(ci,/pos-site-isolation-checks\.sql/);
  assert.match(integration,/pos-site-isolation-checks\.sql/);
  assert.match(sql,/BEGIN;[\s\S]*ROLLBACK;/);
  assert.match(sql,/pos_cashier\.ensure_admin_academy_pos_site/);
  assert.match(sql,/pos_cashier\.set_admin_terminal_operator/);
  assert.match(sql,/pos_cashier\.read_admin_pos_site_finances/);
  assert.match(sql,/pos_cashier\.read_admin_pos_site_sales/);
  assert.match(sql,/location_manager_read_sibling_finances/);
  assert.match(sql,/city_manager_read_other_city_finances/);
  assert.match(sql,/revoked_manager_still_owns_assigned_cashier_register/);
  assert.match(sql,/revoked_admin_session_still_read_pos_site_directory/);
  // One unpaid sale proves recovery refusal inside a rollback-on-error block.
  // Sales are immutable: a synthetic fixture must NEVER be deleted.
  assert.match(sql,/INSERT INTO pos_cashier\.sales\(/);
  assert.match(sql,/synthetic_pending_sale_subtransaction_not_rolled_back/);
  assert.doesNotMatch(sql,/DELETE FROM pos_cashier\.sales/);
  assert.match(sql,/supervisor_recovered_shift_with_pending_sale/);
  assert.doesNotMatch(sql,/INSERT INTO pos_cashier\.(payment_events|receipts|refund_events)/);
});
