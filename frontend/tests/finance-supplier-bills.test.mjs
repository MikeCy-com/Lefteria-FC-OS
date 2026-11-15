import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115264000_FinanceSupplierBillRegister.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Finance/FinanceSupplierBillEndpoints.cs');
const grants=read('ops/postgres/finance-supplier-bill-grants.sql');
const checks=read('ops/postgres/finance-supplier-bill-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const main=read('frontend/src/routes/admin/finance/+page.svelte');
const page=read('frontend/src/lib/admin/AdminSupplierBills.svelte');
const territories=read('frontend/src/routes/admin/territories/+page.svelte');
const sites=read('frontend/src/lib/admin/AcademySupplierSiteTotals.svelte');

test('supplier bills are location-attributed, immutable and never classified as settled',()=>{
  for(const mark of [
    'CREATE TABLE finance.supplier_bills','UNIQUE(organization_id,client_request_id)',
    'ux_supplier_bills_reference','BEFORE UPDATE OR DELETE',
    'ENABLE ROW LEVEL SECURITY','REFERENCES club.football_locations(city_id,id)',
    "organization_id='lefteria_fc_academy'",
    'finance.authorize_admin_supplier_bills(_digest,_owner)',
    'finance.create_admin_supplier_bill(','finance.read_admin_supplier_bills(',
    'finance.read_admin_supplier_site_totals(','club.admin_football_allowed(',
    "'cash_paid',false","'posted_to_general_ledger',false",
    "'is_cash_payment',false","'is_general_ledger',false"
  ])assert.ok(migration.includes(mark),mark);
  assert.doesNotMatch(migration,/UPDATE finance\.supplier_bills\s+SET/);
  assert.doesNotMatch(migration,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON finance\.supplier_bills/);
});

test('DB interface requires exact accountant owner scope and independently checks manager city and site',()=>{
  for(const mark of [
    "session.User.Role is not (\"super_admin\" or \"accountant\")",
    'AdminPermissions.Has(session.User.Role, write ? "finance:write" : "finance:read")',
    'session.User.Organizations.Any(o => o.Id == owner)',
    'secrets.TokenDigest(cookie)',"context.Request.Query[\"organization_id\"].Count != 1",
    'AdminFootballTerritoryEndpoints.Verify(',
    'finance.read_admin_supplier_site_totals',
    'finance.create_admin_supplier_bill',
    'RequireAuthorization(WebAdminServices.Policy)',
    'ExcludeFromDescription()'
  ])assert.ok(endpoint.includes(mark),mark);
  for(const mark of [
    'has_any_column_privilege','GRANT EXECUTE ON FUNCTION',
    'REVOKE ALL ON FUNCTION','FROM :"app_role"',
    'finance.authorize_admin_supplier_bills(text,text)'
  ])assert.ok(grants.includes(mark),mark);
});

test('isolation SQL proves idempotence, immutable invoices, location filtering, revoked access',()=>{
  for(const mark of [
    'supplier_bill_false_payment_claim','supplier_bill_idempotency_broken',
    'supplier_bill_request_mutated','supplier_bill_updated_in_place',
    'supplier_bill_deleted_in_place','duplicate_vendor_invoice_accepted',
    'site_manager_supplier_leak','first_team_academy_supplier_leak',
    'manager_recorded_supplier_bill','first_team_accountant_read_academy_bills',
    'academy_bill_without_location','first_team_took_academy_site',
    'revoked_accountant_read_vendor_records','supplier_bill_missing_audit',
    'ROLLBACK;'
  ])assert.ok(checks.includes(mark),mark);
  assert.match(workflow,/finance-supplier-bill-grants\.sql/);
  assert.match(workflow,/finance-supplier-bill-checks\.sql/);
});

test('accountant and location manager panels compile and do not invent paid status',()=>{
  for(const [path,code] of [
    ['admin/finance/+page.svelte',main],['AdminSupplierBills.svelte',page],
    ['admin/territories/+page.svelte',territories],
    ['AcademySupplierSiteTotals.svelte',sites]
  ])assert.ok(compile(code,{filename:path,generate:'server'}).js.code.length>100,path);
  assert.match(main,/AdminSupplierBills/);
  assert.match(page,/data-testid="supplier-bill-register"/);
  assert.match(page,/client_request_id:key/);
  assert.match(page,/location_id:selected==='lefteria_fc_academy'/);
  assert.match(page,/cash_paid/);
  assert.match(territories,/AcademySupplierSiteTotals/);
  assert.match(sites,/data-testid="academy-site-supplier-totals"/);
  assert.match(sites,/is_cash_payment!==false/);
});
