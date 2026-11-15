import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115266000_FinanceSupplierCreditNotes.cs');
const api=read('backend/src/LefteriaFc.Api/Finance/FinanceSupplierCreditEndpoints.cs');
const positionApi=read('backend/src/LefteriaFc.Api/Finance/FinanceSupplierPaymentEndpoints.cs');
const grants=read('ops/postgres/finance-supplier-credits-grants.sql');
const checks=read('ops/postgres/finance-supplier-credits-checks.sql');
const workflow=read('.github/workflows/rewrite-build.yml');
const allowlist=read('ops/postgres/web-admin-auth-permissions-check.sql');
const panel=read('frontend/src/lib/admin/AdminSupplierPaymentReview.svelte');
const credits=read('frontend/src/lib/admin/AdminSupplierCredits.svelte');
const program=read('backend/src/LefteriaFc.Api/Program.cs');

test('supplier credit notes are immutable and separated from cash, VAT and ledger posting',()=>{
 for(const marker of [
  'CREATE TABLE finance.supplier_credit_requests',
  'CREATE TABLE finance.supplier_credit_decisions',
  'ENABLE ROW LEVEL SECURITY',
  'CREATE TRIGGER trg_supplier_credit_request_immutable',
  'CREATE TRIGGER trg_supplier_credit_decision_immutable',
  'CREATE FUNCTION finance.create_admin_supplier_credit_request(',
  'CREATE FUNCTION finance.review_admin_supplier_credit_request(',
  'CREATE FUNCTION finance.read_admin_supplier_payable_position(',
  'finance.authorize_admin_supplier_bills(_digest,_owner)',
  "v_role<>'super_admin'",
  'v_credit.requested_by=v_actor',
  'FOR UPDATE;',
  'v_credit.amount_cents>v_gross-v_paid-v_credits',
  "'bank_refund_recorded',false",
  "'posted_to_general_ledger',false",
  "coalesce((SELECT d.decision",
  "'approved_credit_cents',v_credit_sum",
  "'net_payables_cents',v_gross-v_credit_sum",
  "'remaining_cents',v_gross-v_credit_sum-v_paid"
 ])assert.ok(migration.includes(marker),marker);
 assert.doesNotMatch(migration,/UPDATE finance\.supplier_credit_(requests|decisions) SET/);
 assert.doesNotMatch(migration,/payabl\.com|stripe\.com|Twilio|http_client/);
});

test('previous payment approval path is protected by credits in the same transaction',()=>{
 for(const marker of [
  'CREATE FUNCTION finance.check_supplier_payment_credit_ceiling()',
  'CREATE TRIGGER trg_supplier_payment_credit_ceiling',
  'BEFORE INSERT ON finance.supplier_payment_decisions',
  'FOR UPDATE OF b;',
  'v_amount>v_gross-v_existing-v_credits',
  "d.decision='approved_manual'",
  "finance.read_admin_supplier_payment_register(",
  "jsonb_array_elements(v_report->'bills')",
  "SELECT coalesce(sum(c.amount_cents) FILTER(",
  'coalesce(jsonb_agg('
 ])assert.ok(migration.includes(marker),marker);
 assert.match(positionApi,/finance\.read_admin_supplier_payable_position\(/);
});

test('credit API enforces owner/granted session and explicit independent decision',()=>{
 for(const marker of [
  'FinanceSupplierBillEndpoints.Verify(',
  'FinanceSupplierBillEndpoints.Run(',
  'RequireAuthorization(WebAdminServices.Policy)',
  'session?.User.Role != "super_admin"',
  'body.Approve is null',
  'secrets.TokenDigest(cookie)',
  'RequestAdminSupplierCredit',
  'ReviewAdminSupplierCredit',
  'ExcludeFromDescription()'
 ])assert.ok(api.includes(marker),marker);
 assert.match(program,/app\.MapAdminSupplierCredits\(\)/);
 for(const marker of [
  'has_any_column_privilege',
  "FROM :\"app_role\"",
  'finance.create_admin_supplier_credit_request',
  'finance.review_admin_supplier_credit_request',
  'finance.read_admin_supplier_payable_position',
  'finance.check_supplier_payment_credit_ceiling()'
 ])assert.ok(grants.includes(marker),marker);
 assert.match(allowlist,/finance\.read_admin_supplier_payable_position/);
 assert.match(workflow,/finance-supplier-credits-grants\.sql/);
 assert.match(workflow,/finance-supplier-credits-checks\.sql/);
});

test('SQL tests exercise organization grants, four-eyes and net liabilities',()=>{
 for(const marker of [
  'credit_note_claimed_refund_or_gl',
  'supplier_credit_idempotent_replay_invalid',
  'pending_credit_wrongly_reduced_balance',
  'accountant_approved_supplier_credit',
  'location_manager_approved_supplier_credit',
  'manager_read_supplier_credit_notes',
  'first_team_accountant_created_academy_credit',
  'academy_accountant_credited_first_team_bill',
  'credit_replay_changed_amount',
  'duplicate_supplier_credit_reference_accepted',
  'approved_supplier_credit_claimed_cash_or_gl',
  'supplier_credit_decision_replay_invalid',
  'supplier_credit_decision_changed',
  'supplier_credit_net_payables_incorrect',
  'approved_supplier_credit_failed_to_limit_payment',
  'credit_approval_exceeded_remaining_payable',
  'super_admin_approved_own_credit_note',
  'second_credit_not_reflected',
  'supplier_credit_immutable_request_broken',
  'supplier_credit_immutable_decision_broken',
  'revoked_accountant_read_credits',
  'supplier_credit_missing_audit_event',
  'ROLLBACK;'
 ])assert.ok(checks.includes(marker),marker);
});

test('Svelte compiles and displays different gross, approved credit, net and pending balances',()=>{
 assert.ok(compile(panel,{filename:'AdminSupplierPaymentReview.svelte',generate:'server'}).js.code.length>100);
 assert.ok(compile(credits,{filename:'AdminSupplierCredits.svelte',generate:'server'}).js.code.length>100);
 for(const marker of [
  'AdminSupplierCredits','data.credit_notes','approved_credit_cents',
  'net_payables_cents','pending_credit_cents',
  'bank_refund_recorded!==false',
  'net_payables_cents!==summary.approved_manual_cents+summary.remaining_cents'
 ])assert.ok(panel.includes(marker),marker);
 for(const marker of [
  'data-testid="supplier-credit-corrections"',
  'available=$derived(credits.filter',
  'requested_by!==sessionId',
  'window.confirm',
  'onSaved()',
  'X-Lefteria-Admin',
  "bank_refund_recorded!==false",
  "'/api/admin/finance/supplier-credits'"
 ])assert.ok(credits.includes(marker),marker);
});
