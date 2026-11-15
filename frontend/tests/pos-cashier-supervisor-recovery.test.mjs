import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115951000_PosSuspendedCashierShiftRecovery.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Pos/AdminPosSiteEndpoints.cs');
const panel=read('frontend/src/lib/admin/AdminPosSitesPanel.svelte');
const sql=read('ops/postgres/pos-site-isolation-checks.sql');

test('supervisor recovery is isolated to authenticated super admins and canonical site',()=>{
  assert.match(endpoint,/routes\.MapPost\("\/shifts\/\{shiftId:guid\}\/recover-close"/);
  assert.match(endpoint,/context, true, true, true, ct/);
  assert.match(endpoint,/pos_cashier\.recover_suspended_admin_cashier_shift/);
  assert.match(migration,/FROM club\.authorize_admin_football\(_digest,_owner\)/);
  assert.match(migration,/v_role<>'super_admin'/);
  assert.match(migration,/pos_cashier\.authorize_site_finances\(_digest,_owner,v_location\)/);
  assert.match(migration,/v_target_role IS DISTINCT FROM 'cashier' OR v_target_active IS DISTINCT FROM false/);
  assert.match(sql,/city_manager_force_closed_cashier_shift/);
  assert.match(sql,/supervisor_cross_org_shift_recovery/);
  assert.match(sql,/active_cashier_shift_force_closed_by_super_admin/);
});
test('supervisor must enter physical cash, give reason and confirm, with replay-safe audit',()=>{
  assert.match(endpoint,/CountedCashCents is < 0 or > 100000000/);
  assert.match(endpoint,/account_suspended" or "security_incident" or "operator_unavailable"/);
  assert.match(migration,/close_request_id=_request/);
  assert.match(migration,/v_shift\.close_request_id IS DISTINCT FROM _request/);
  assert.match(migration,/counted_cash_cents=_counted/);
  assert.match(migration,/variance_cents=_counted-v_expected/);
  assert.match(migration,/shift_recovery_close/);
  assert.match(migration,/audit\.security_events/);
  assert.match(migration,/pos_shift_recovery_requires_forward_recovery/);
  assert.match(panel,/data-testid="pos-shift-supervisor-recovery"/);
  assert.match(panel,/bind:checked=\{recoveryConfirmed\}/);
  assert.match(panel,/crypto\.randomUUID\(\)/);
  assert.match(panel,/v\.variance_cents!==cents-Number\(v\.expected_cash_cents\)/);
  assert.match(sql,/supervisor_recovery_replay_not_idempotent/);
  assert.match(sql,/supervisor_recovery_fabricated_payment_or_receipt/);
});
test('unsettled sales are not changed by emergency recovery',()=>{
  assert.match(migration,/pos_shift_recovery_ledger_unresolved/);
  assert.match(migration,/sale\.status NOT IN \('paid','voided'\)/);
  assert.match(migration,/rs\.released_at IS NULL AND rs\.consumed_at IS NULL/);
  assert.match(migration,/payment\.status='pending'/);
  assert.match(migration,/refund\.status='pending'/);
  assert.doesNotMatch(migration,/UPDATE pos_cashier\.sales/);
  assert.doesNotMatch(migration,/INSERT INTO pos_cashier\.payment_events/);
  assert.doesNotMatch(migration,/INSERT INTO pos_cashier\.receipts/);
});
