import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115263000_FinanceAccountingOverview.cs');
const api=read('backend/src/LefteriaFc.Api/Finance/FinanceOverviewEndpoints.cs');
const grants=read('ops/postgres/finance-accounting-overview-grants.sql');
const fixtures=read('ops/postgres/finance-accounting-overview-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const page=read('frontend/src/routes/admin/finance/+page.svelte');
const panel=read('frontend/src/lib/admin/AdminFinanceOverview.svelte');
const files=['Payabl/PayablEndpoints.cs','Payabl/AcademyFeeEndpoints.cs',
  'Payabl/AcademyInvoiceEndpoints.cs']
  .map(file=>read('backend/src/LefteriaFc.Api/'+file));

test('private aggregate financial reports never combine overlapping fee and charge ledgers',()=>{
  assert.match(migration,/CREATE FUNCTION finance\.read_admin_accounting_overview/);
  assert.match(migration,/v_role NOT IN \('super_admin','accountant'\)/);
  assert.match(migration,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(migration,/a\.is_active AND NOT a\.invite_pending/);
  assert.match(migration,/g\.organization_id=_owner/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/academy_training_fees/);
  assert.match(migration,/general_charge_balances/);
  assert.match(migration,/payabl_sandbox/);
  assert.match(migration,/is_statutory_statement',false/);
  assert.doesNotMatch(migration,/\b(UPDATE|DELETE|INSERT) finance\.(training_fee_issues|payment_requests)/);
  assert.doesNotMatch(migration,/grand_total|revenue_total|net_profit/);
});

test('accountant overview has two independently authorized scopes and no bypass for managers',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/session\.User\.Role is not \("super_admin" or "accountant"\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role,"finance:read"\)/);
  assert.match(api,/session\.User\.Organizations\.Any\(o=>o\.Id==organization_id\)/);
  assert.match(api,/secrets\.TokenDigest\(cookie!\)/);
  assert.match(api,/CacheControl="private, no-store"/);
  assert.match(api,/ExcludeFromDescription\(\)/);
  for(const backend of files){
    assert.match(backend,/session\.User\.Role != "super_admin"/);
  }
  assert.match(grants,/has_any_column_privilege/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION/);
  assert.match(grants,/REVOKE ALL ON FUNCTION/);
  assert.match(grants,/FROM :"app_role"/);
});

test('SQL fixture enforces independent ledger math, revocation and org boundaries',()=>{
  for(const marker of [
    'accountant_overview_invalid_or_leaked_personal_data',
    'first_team_academy_finance_leak',
    'location_manager_received_organization_finance',
    'accountant_other_organization_leak',
    'revoked_accountant_retrieved_finances',
    'ROLLBACK'
  ])assert.ok(fixtures.includes(marker));
  assert.match(workflow,/finance-accounting-overview-grants\.sql/);
  assert.match(workflow,/finance-accounting-overview-checks\.sql/);
});

test('Svelte finance overview stays true to scope and does not pretend to be statutory',()=>{
  assert.ok(compile(page,{filename:'admin/finance/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(panel,{filename:'AdminFinanceOverview.svelte',generate:'server'}).js.code.length>100);
  assert.match(page,/finance-organization-tabs/);
  assert.match(page,/providerAccess/);
  assert.match(page,/!providerAccess/);
  assert.match(page,/AdminFinanceOverview/);
  assert.match(page,/finance-scoped-access/);
  assert.match(panel,/data-testid="accounting-overview"/);
  assert.match(panel,/credentials:'same-origin',cache:'no-store'/);
  assert.match(panel,/is_statutory_statement===false/);
  assert.doesNotMatch(panel,/\{euro\([^)]*\.issued_cents\s*\+\s*/);
});
