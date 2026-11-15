import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115265000_FinanceSupplierPaymentReview.cs');
const api=read('backend/src/LefteriaFc.Api/Finance/FinanceSupplierPaymentEndpoints.cs');
const grants=read('ops/postgres/finance-supplier-payments-grants.sql');
const checks=read('ops/postgres/finance-supplier-payments-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const allowlist=read('ops/postgres/web-admin-auth-permissions-check.sql');
const page=read('frontend/src/routes/admin/finance/+page.svelte');
const panel=read('frontend/src/lib/admin/AdminSupplierPaymentReview.svelte');
const program=read('backend/src/LefteriaFc.Api/Program.cs');

test('manual supplier evidence is two-stage, immutable and not a provider or GL claim',()=>{
  for(const marker of [
    'CREATE TABLE finance.supplier_payment_requests',
    'CREATE TABLE finance.supplier_payment_decisions',
    'BEFORE UPDATE OR DELETE ON finance.supplier_payment_requests',
    'BEFORE UPDATE OR DELETE ON finance.supplier_payment_decisions',
    'finance.authorize_admin_supplier_bills(_digest,_owner)',
    'CREATE FUNCTION finance.create_admin_supplier_payment_request(',
    'CREATE FUNCTION finance.review_admin_supplier_payment_request(',
    'CREATE FUNCTION finance.read_admin_supplier_payment_register(',
    'v_payment.requested_by=v_actor',
    "v_role<>'super_admin'",
    'FOR UPDATE;',
    'v_payment.amount_cents>v_bill_amount-v_approved',
    "d.decision='approved_manual'",
    "coalesce((SELECT d.decision",
    "'bank_verified',false",
    "'is_general_ledger',false",
    "'posted_to_general_ledger',false",
    'ENABLE ROW LEVEL SECURITY'
  ])assert.ok(migration.includes(marker),marker);
  assert.doesNotMatch(migration,/UPDATE finance\.supplier_payment_(requests|decisions) SET/);
  assert.doesNotMatch(migration,/http_client|payabl\.com|Twilio/);
});

test('payment APIs revalidate exact owner and allow only superadmin reviewers',()=>{
  for(const marker of [
    'RequireAuthorization(WebAdminServices.Policy)',
    'FinanceSupplierBillEndpoints.Verify(',
    'FinanceSupplierBillEndpoints.Run(',
    'FinanceSupplierBillEndpoints.Month(month)',
    'session?.User.Role != "super_admin"',
    'secrets.TokenDigest(cookie)',
    'CreateAdminSupplierPaymentEvidence',
    'ReviewAdminSupplierPaymentEvidence',
    'ReadAdminSupplierPaymentEvidence',
    'ExcludeFromDescription()'
  ])assert.ok(api.includes(marker),marker);
  assert.match(program,/app\.MapAdminSupplierPayments\(\)/);
  assert.match(grants,/has_any_column_privilege/);
  assert.match(grants,/finance\.review_admin_supplier_payment_request/);
  assert.match(grants,/FROM :"app_role"/);
  assert.match(allowlist,/finance\.read_admin_supplier_payment_register/);
  assert.match(workflow,/finance-supplier-payments-grants\.sql/);
  assert.match(workflow,/finance-supplier-payments-checks\.sql/);
});

test('database fixture covers 4-eyes, partial amounts, overpayment and isolation',()=>{
  for(const marker of [
    'unreviewed_payment_claimed_paid',
    'payment_request_replay_invalid',
    'conflicting_payment_request_accepted',
    'pending_payment_must_not_count_as_paid',
    'accountant_approved_own_evidence',
    'site_manager_leaked_supplier_payment_evidence',
    'first_team_accountant_accessed_academy',
    'manual_review_claimed_bank_or_gl_posting',
    'review_decision_mutable',
    'supplier_bill_overpayment_accepted',
    'approved_rejected_partial_totals_wrong',
    'super_admin_approved_own_request',
    'four_eyes_partial_allocation_invalid',
    'first_team_supplier_payment_leak',
    'supplier_payment_evidence_updated',
    'supplier_payment_approval_deleted',
    'revoked_accountant_read_payment_evidence',
    'supplier_payment_audit_events_missing',
    'ROLLBACK;'
  ])assert.ok(checks.includes(marker),marker);
});

test('svelte accounting review renders guarded workflow rather than invented paid receipts',()=>{
  assert.ok(compile(page,{filename:'admin/finance/+page.svelte',generate:'server'}).js.code.length>100);
  assert.ok(compile(panel,{filename:'AdminSupplierPaymentReview.svelte',generate:'server'}).js.code.length>100);
  assert.match(page,/AdminSupplierPaymentReview/);
  assert.match(page,/canReview=\{\$auth\.session\.user\.role === 'super_admin'\}/);
  assert.match(panel,/data-testid="supplier-payment-manual-review"/);
  assert.match(panel,/bank_verified!==false/);
  assert.match(panel,/posted_to_general_ledger!==false/);
  assert.match(panel,/requested_by===actor/);
  assert.match(panel,/credentials:'same-origin',cache:'no-store'/);
  assert.match(panel,/X-Lefteria-Admin/);
  assert.match(panel,/window\.confirm/);
});
