import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115269000_FinanceSupplierSitePayablesWithReversals.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Finance/FinanceSupplierBillEndpoints.cs');
const grants=read('ops/postgres/finance-supplier-site-payables-grants.sql');
const fixture=read('ops/postgres/finance-supplier-site-payables-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const allowlist=read('ops/postgres/web-admin-auth-permissions-check.sql');
const sites=read('frontend/src/lib/admin/AcademySupplierSiteTotals.svelte');
const territories=read('frontend/src/routes/admin/territories/+page.svelte');

test('site payable reader returns only authorized location identifiers and totals',()=>{
  for(const mark of [
    'CREATE FUNCTION finance.read_admin_supplier_site_payables(',
    'SECURITY DEFINER',
    'SET search_path=pg_catalog,pg_temp',
    'club.authorize_admin_football(_digest,_owner)',
    "v_actor,v_role,_owner,s.id,l.id,false",
    "'lefteria_fc_academy'",
    "b.location_id=l.id AND b.city_id=l.city_id",
    'finance.supplier_payment_requests r',
    'finance.supplier_payment_decisions d',
    'finance.supplier_credit_requests c',
    'finance.supplier_credit_decisions d',
    "WHERE d.decision='approved_manual'",
    "WHERE d.request_id IS NULL",
    "'gross_amount_cents',m.gross_cents",
    "'approved_credit_cents',m.credit_cents",
    "'pending_credit_cents',m.pending_credit_cents",
    "'net_payables_cents',m.gross_cents-m.credit_cents",
    "'approved_manual_payment_cents',m.payment_cents",
    "'pending_payment_evidence_cents',m.pending_payment_cents",
    "'remaining_cents',m.gross_cents-m.credit_cents-m.payment_cents",
    "'bank_verified',false",
    "'is_cash_payment',false",
    "'posted_to_general_ledger',false",
    "'reporting_basis','supplier_bill_date'",
    "'approval_reversal_adjusted',true",
    "rev.target_kind='payment'",
    "rev.target_kind='credit'",
    "revd.decision='approved_manual'",
    'finance.supplier_review_reversal_requests',
    'finance.supplier_review_reversal_decisions',
    'FROM PUBLIC;'
  ])assert.ok(migration.includes(mark),mark);
  const body=migration.slice(migration.indexOf('CREATE FUNCTION'));
  for(const sensitive of ["'supplier_name'","'invoice_reference'","'credit_reference'","'evidence_reference'","'recorded_by'","'requested_by'"]){
    assert.ok(!body.includes(sensitive),'must not return '+sensitive);
  }
  assert.doesNotMatch(migration,/UPDATE finance\.supplier_bills SET/);
});

test('Academy manager endpoint uses organization-specific session auth and protected DB function',()=>{
  for(const mark of [
    '/api/admin/football/territories/finance/supplier-payables',
    'AdminFootballTerritoryEndpoints.Verify(',
    'context.Request.Query["organization_id"].Count != 1',
    'organization_id != "lefteria_fc_academy"',
    'finance.read_admin_supplier_site_payables(',
    'secrets.TokenDigest(cookie)',
    'RequireAuthorization(WebAdminServices.Policy)',
    'ExcludeFromDescription()'
  ])assert.ok(endpoint.includes(mark),mark);
  for(const mark of [
    'has_any_column_privilege',
    'finance.supplier_payment_requests',
    'finance.supplier_credit_requests',
    'GRANT EXECUTE ON FUNCTION finance.read_admin_supplier_site_payables',
    'REVOKE ALL ON FUNCTION finance.read_admin_supplier_site_payables',
    'FROM :"app_role"'
  ])assert.ok(grants.includes(mark),mark);
  assert.match(allowlist,/finance\.read_admin_supplier_site_payables\(text,text,date\)/);
  assert.match(workflow,/finance-supplier-site-payables-grants\.sql/);
  assert.match(workflow,/finance-supplier-site-payables-checks\.sql/);
});

test('roll-back SQL covers same-city site isolation, crossed organizations and live revocation',()=>{
  for(const mark of [
    'location_manager_payable_scope_or_redaction_failed',
    'location_manager_payable_balances_incorrect',
    'same_city_other_location_payables_exposed',
    'super_admin_payable_site_breakdown_wrong',
    'first_team_accountant_viewed_academy_site',
    'first_team_took_academy_site_payables',
    'non_first_day_month_allowed',
    'null_month_allowed',
    'pending_reversal_changed_site_payables',
    'site_original_payment_reviewer_approved_reversal',
    'payment_reversal_not_applied_to_only_own_site',
    'rejected_credit_reversal_changed_site_payables',
    'credit_reversal_not_applied_to_only_own_site',
    'same_city_other_location_reversal_leaked',
    'reversal_in_other_site_changed_my_balance',
    'academy_reversal_changed_first_team_or_other_city',
    'revoked_site_manager_read_payables',
    'approved_credit_cents',
    'pending_credit_cents',
    'approved_manual_payment_cents',
    'pending_payment_evidence_cents',
    'supplier_credit',
    'supplier_payment',
    'ROLLBACK;'
  ])assert.ok(fixture.includes(mark),mark);
});

test('existing Academy territory panel compiles and never claims manual approval is verified bank cash',()=>{
  for(const [name,source] of [
    ['AcademySupplierSiteTotals.svelte',sites],
    ['admin/territories/+page.svelte',territories]
  ])assert.ok(compile(source,{filename:name,generate:'server'}).js.code.length>100,name);
  for(const mark of [
    'data-testid="academy-site-supplier-totals"',
    '/api/admin/football/territories/finance/supplier-payables?',
    'row.gross_amount_cents===',
    'row.net_payables_cents===',
    'posted_to_general_ledger!==false',
    'bank_verified!==false',
    'approval_reversal_adjusted!==true',
    'approved_credit_cents',
    'pending_credit_cents',
    'approved_manual_payment_cents',
    'pending_payment_evidence_cents',
    'remaining_cents',
    'Object.keys(row).every(k=>keys.has(k))',
    "city.reduce((total,entry)=>total+entry[key],0)"
  ])assert.ok(sites.includes(mark),mark);
  assert.match(territories,/AcademySupplierSiteTotals/);
  assert.ok(!sites.includes('supplier_name'),'vendor names are not returned to managers');
  assert.ok(!sites.includes('invoice_reference'),'invoice references are not returned to managers');
});
