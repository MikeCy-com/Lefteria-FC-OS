import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261122235500_AcademyFutureFeeCancellation.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Payabl/AcademyFeeEndpoints.cs');
const grants=read('ops/postgres/payabl-finance-grants.sql');
const checks=read('ops/postgres/academy-future-fee-cancellation-checks.sql');
const pipeline=read('.github/workflows/rewrite-build.yml');
const finance=read('frontend/src/lib/admin/AdminTrainingFees.svelte');
const overview=read('frontend/src/lib/admin/AdminFinanceOverview.svelte');
const site=read('frontend/src/lib/admin/AcademyLocationFinancePanel.svelte');
const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');

test('cancellation is an audited retained status, future-only and denied on paid/provider-linked fees',()=>{
  for(const marker of [
    '20261122235500_AcademyFutureFeeCancellation',
    "status IN ('issued','paid','review_required','cancelled')",
    'cancel_request_id uuid UNIQUE',
    'CREATE FUNCTION finance.cancel_admin_future_training_fee',
    'identity.lock_admin_club_authority(_digest,_owner)',
    "v_role<>'super_admin'",
    "clock_timestamp() AT TIME ZONE 'Asia/Nicosia'",
    'v_row.payment_request_id IS NOT NULL',
    'finance.training_fee_cash_receipts',
    'finance.training_fee_sms_outbox',
    'admin.finance.training_fee.cancel_future',
    'cancelled_fees_require_forward_recovery'
  ])assert.ok(migration.includes(marker),marker);
  assert.doesNotMatch(migration,/DELETE FROM finance\.training_fee_issues/);
  assert.match(migration,/v_row\.cancel_request_id=_request/);
  assert.match(migration,/f\.status<>'cancelled'/);
});

test('cancelled debts are excluded from active financial statistics but retained as audit totals',()=>{
  assert.match(migration,/'cancelled_cents'/);
  assert.match(migration,/'cancelled_count'/);
  assert.match(migration,/'training_cancelled_cents'/);
  assert.match(migration,/'training_cancelled_count'/);
  assert.match(migration,/sum\(f\.amount_cents\) FILTER\(WHERE f\.status<>'cancelled'\)/);
  assert.match(migration,/sum\(i\.amount_cents\) FILTER\(WHERE i\.status<>'cancelled'\)/);
  assert.match(migration,/sum\(f\.amount_cents\)\s+FILTER\(WHERE f\.status='paid'\)/);
  assert.match(migration,/WHERE payer_identity_id=_identity AND status<>'cancelled'/);
});

test('only Super Admin can invoke cancellation; no provider call, raw delete or hidden permission upgrade',()=>{
  assert.match(endpoint,/CancelAcademyFutureFeeCommand/);
  assert.match(endpoint,/admin\.MapPost\("\/\{feeId:guid\}\/cancel"/);
  assert.match(endpoint,/finance\.cancel_admin_future_training_fee/);
  assert.match(endpoint,/.ExcludeFromDescription\(\)/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION finance\.cancel_admin_future_training_fee/);
  assert.match(grants,/lefteria\.finance_app_role/);
  assert.doesNotMatch(endpoint,/DELETE FROM finance\.training_fee_issues/);
});

test('Svelte admin views compile with cancellation actions, finance sums and player-scoped links',()=>{
  for(const [name,file] of [
    ['AdminTrainingFees.svelte',finance],
    ['AdminFinanceOverview.svelte',overview],
    ['AcademyLocationFinancePanel.svelte',site],
    ['AdminRosterPlayersPanel.svelte',roster]
  ])assert.ok(compile(file,{filename:name,generate:'server'}).js.code.length>100,name);
  assert.match(finance,/data-testid="admin-fee-cancel-editor"/);
  assert.match(finance,/data-testid="admin-cancel-future-fee"/);
  assert.match(finance,/cancellationRequestIds\[fee\.id\]/);
  assert.match(finance,/fee\.status==='cancelled'/);
  assert.match(overview,/academy-fees-cancelled-stat/);
  assert.match(site,/training_cancelled_count/);
  assert.match(roster,/allowFinance && owner === 'lefteria_fc_academy'/);
});

test('disposable database checks cover auditable cancellation, receipts, role safety and CI execution',()=>{
  for(const marker of [
    'site_manager_cancelled_global_fee',
    'identical_retry_not_idempotent',
    'cancelled_future_fee_pollutes_accounting',
    'paid_cash_fee_cancelled',
    'paid_cash_receipt_disappeared',
    'provider_linked_fee_cancelled_without_reconciliation',
    'ROLLBACK'
  ])assert.ok(checks.includes(marker),marker);
  assert.match(pipeline,/academy-future-fee-cancellation-checks\.sql/);
});
