import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115268000_FinanceSupplierApprovalReversals.cs');
const api=read('backend/src/LefteriaFc.Api/Finance/FinanceSupplierReversalEndpoints.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const grants=read('ops/postgres/finance-supplier-reversal-grants.sql');
const fixture=read('ops/postgres/finance-supplier-reversal-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const allowlist=read('ops/postgres/web-admin-auth-permissions-check.sql');
const parent=read('frontend/src/lib/admin/AdminSupplierPaymentReview.svelte');
const reversals=read('frontend/src/lib/admin/AdminSupplierApprovalReversals.svelte');
const credits=read('frontend/src/lib/admin/AdminSupplierCredits.svelte');

test('immutable reversal records require a second Super Admin separate from original approver',()=>{
  for(const marker of [
    'CREATE TABLE finance.supplier_review_reversal_requests',
    'CREATE TABLE finance.supplier_review_reversal_decisions',
    'ENABLE ROW LEVEL SECURITY',
    'trg_supplier_review_reversal_request_immutable',
    'trg_supplier_review_reversal_decision_immutable',
    'CREATE FUNCTION finance.create_admin_supplier_approval_reversal(',
    'CREATE FUNCTION finance.review_admin_supplier_approval_reversal(',
    'finance.authorize_admin_supplier_bills(_digest,_owner)',
    'v_reversal.requested_by=v_actor',
    'v_original_reviewer=v_actor',
    "v_role<>'super_admin'",
    'FOR UPDATE',
    'supplier_reversal_duplicate_approval',
    "r.target_kind=v_reversal.target_kind",
    "d.decision='approved_manual'",
    'supplier_reversal_request_id_conflict',
    'supplier_reversal_decision_conflict',
    'admin.finance.supplier_reversal.request',
    'admin.finance.supplier_reversal.review',
    "'bank_verified',false",
    "'bank_refund_recorded',false",
    "'posted_to_general_ledger',false"
  ])assert.ok(migration.includes(marker),marker);
  assert.doesNotMatch(migration,/UPDATE finance\.supplier_review_reversal_(requests|decisions) SET/);
  assert.doesNotMatch(migration,/payabl\.com|stripe\.com|http_client|Twilio/);
});

test('effective payments and credits are recalculated across all existing approval guards',()=>{
  for(const name of [
    'review_admin_supplier_payment_request',
    'read_admin_supplier_payment_register',
    'check_supplier_payment_credit_ceiling',
    'review_admin_supplier_credit_request',
    'read_admin_supplier_payable_position'
  ]){
    const head='CREATE OR REPLACE FUNCTION finance.'+name+'(';
    assert.ok(migration.includes(head),'missing upgrade '+head);
    assert.ok(migration.split(head).length===3,'missing safe Down restoration '+head);
  }
  for(const marker of [
    "rev.target_kind='payment' AND rev.target_id=r.id",
    "rev.target_kind='credit' AND rev.target_id=c.id",
    'finance.supplier_review_reversal_decisions revd',
    "revd.decision='approved_manual'",
    'AS reversed_manual',
    "'reversals',v_reversals",
    "WHERE rev.organization_id=_owner",
    "'approved_credit_cents',v_credit_sum",
    "'remaining_cents',v_gross-v_credit_sum-v_paid"
  ])assert.ok(migration.includes(marker),marker);
  assert.ok(migration.includes('DROP TABLE finance.supplier_review_reversal_requests;'));
});

test('API validates exact finance owner, active account session and independent reviewer',()=>{
  for(const marker of [
    'FinanceSupplierBillEndpoints.Verify(',
    'FinanceSupplierBillEndpoints.Run(',
    'RequireAuthorization(WebAdminServices.Policy)',
    'session?.User.Role != "super_admin"',
    'secrets.TokenDigest(cookie)',
    'body.TargetKind is not ("payment" or "credit")',
    'body.Approve is null',
    'RequestAdminSupplierApprovalReversal',
    'ReviewAdminSupplierApprovalReversal',
    'ExcludeFromDescription()'
  ])assert.ok(api.includes(marker),marker);
  assert.match(program,/app\.MapAdminSupplierReversals\(\)/);
  for(const marker of [
    'has_any_column_privilege',
    'finance.supplier_review_reversal_requests',
    'finance.supplier_review_reversal_decisions',
    'GRANT EXECUTE ON FUNCTION',
    'REVOKE ALL ON FUNCTION',
    'FROM :"app_role"'
  ])assert.ok(grants.includes(marker),marker);
  assert.match(allowlist,/finance\.create_admin_supplier_approval_reversal\(text,text,uuid,text,uuid,text\)/);
  assert.match(allowlist,/finance\.review_admin_supplier_approval_reversal\(text,text,uuid,boolean,text\)/);
  assert.match(workflow,/finance-supplier-reversal-grants\.sql/);
  assert.match(workflow,/finance-supplier-reversal-checks\.sql/);
});

test('synthetic SQL covers replay, replacement payment, same original reviewer denial and isolation',()=>{
  for(const marker of [
    'reversal_baseline_wrong',
    'reversal_request_falsely_reported_paid',
    'reversal_request_replay_invalid',
    'reversal_idempotency_payload_changed',
    'pending_reversal_changed_liability',
    'accountant_approved_reversal',
    'original_reviewer_approved_own_reversal',
    'location_manager_created_supplier_reversal',
    'location_manager_read_supplier_reversals',
    'first_team_accountant_reversed_academy_record',
    'foreign_bill_accepted_as_approved_payment',
    'payment_reversal_review_not_verified',
    'payment_reversal_review_replay_invalid',
    'supplier_reversal_decision_changed',
    'approved_payment_reversal_not_reflected',
    'duplicate_approved_payment_reversal_created',
    'approved_reversal_replay_stale',
    'replacement_payment_after_reversal_failed',
    'rejected_reversal_changed_liability',
    'credit_original_reviewer_cannot_reverse',
    'credit_reversal_not_reflected',
    'duplicate_credit_reversal_created',
    'reversal_effective_liability_not_rebalanced',
    'rejected_original_accepted_for_reversal',
    'reversal_request_was_mutable',
    'reversal_decision_was_mutable',
    'revoked_accountant_read_reversal_register',
    'reversal_audit_events_missing',
    'ROLLBACK;'
  ])assert.ok(fixture.includes(marker),marker);
});

test('accountant and Super Admin reversal interface compile without claiming bank transactions',()=>{
  for(const [name,source] of [
    ['AdminSupplierPaymentReview.svelte',parent],
    ['AdminSupplierApprovalReversals.svelte',reversals],
    ['AdminSupplierCredits.svelte',credits]
  ])assert.ok(compile(source,{filename:name,generate:'server'}).js.code.length>100,name);
  for(const marker of [
    'AdminSupplierApprovalReversals',
    'reversals={data.reversals}',
    'reviewed_by:string|null',
    'reversed_manual:boolean',
    'r.reversals.every',
    'pending_evidence_cents',
    'bank_verified!==false'
  ])assert.ok(parent.includes(marker),marker);
  for(const marker of [
    'data-testid="supplier-approval-reversals"',
    '/api/admin/finance/supplier-reversals?',
    'validTargets',
    'openReviews',
    'source.reviewed_by!==sessionId',
    'pendingTargets',
    'window.confirm',
    'onSaved()',
    'X-Lefteria-Admin',
    'bank_refund_recorded!==false',
    'posted_to_general_ledger!==false'
  ])assert.ok(reversals.includes(marker),marker);
  assert.match(credits,/credit\.reversed_manual\?/);
  assert.match(parent,/request\.reversed_manual\?/);
});
