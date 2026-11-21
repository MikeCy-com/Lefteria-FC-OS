import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=(path)=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115952000_PosIndependentCashCountReview.cs');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosSiteEndpoints.cs');
const panel=read('frontend/src/lib/admin/AdminPosRecoveryReviewPanel.svelte');
const parent=read('frontend/src/lib/admin/AdminPosSitesPanel.svelte');
const fixtures=read('ops/postgres/pos-site-isolation-checks.sql');
const grants=read('ops/postgres/pos-shift-recovery-admin-grants.sql');

test('the independent count is a different Super Admin and cannot cross an organization',()=>{
  assert.match(api,/routes.MapGet\("\/recovery-review-queue"/);
  assert.match(api,/routes.MapPost\("\/shifts\/\{shiftId:guid\}\/independent-count"/);
  assert.match(api,/context,false,true,true,ct/);
  assert.match(api,/context,true,true,true,ct/);
  assert.match(migration,/FROM club.authorize_admin_football\(_digest,_owner\)/);
  assert.match(migration,/v_role<>'super_admin'/);
  assert.match(migration,/v_actor=v_shift.recovery_actor_id OR v_actor=v_shift.cashier_id/);
  assert.match(migration,/pos_cashier.authorize_site_finances\(_digest,_owner,v_location\)/);
  assert.match(fixtures,/first_supervisor_approved_own_cash_count/);
  assert.match(fixtures,/independent_reviewer_crossed_organization/);
  assert.match(fixtures,/city_manager_approved_cash_count/);
});

test('the initial counted cash is withheld until an independently entered second count',()=>{
  assert.match(migration,/CREATE FUNCTION pos_cashier.read_admin_shift_recovery_review_queue/);
  assert.match(panel,/data-testid="pos-independent-recovery-count"/);
  assert.match(panel,/bind:value=\{secondCount\}/);
  assert.match(panel,/bind:checked=\{confirmed\}/);
  assert.match(panel,/recovery_actor_id===actorId/);
  assert.match(parent,/\{#if canProvision\}[\s\S]*<AdminPosRecoveryReviewPanel/);
  const queue=migration.slice(
    migration.indexOf('CREATE FUNCTION pos_cashier.read_admin_shift_recovery_review_queue'),
    migration.indexOf('END $review_queue$;')
  );
  assert.doesNotMatch(queue,/first_count_cents|counted_cash_cents|expected_cash_cents|variance_cents/);
  assert.match(fixtures,/second_count_queue_exposes_or_misses_initial_count/);
});

test('reconciliation evidence is immutable, idempotent and never pays or refunds',()=>{
  assert.match(migration,/CREATE TABLE pos_cashier.shift_recovery_reviews/);
  assert.match(migration,/pos_recovery_reviews_immutable/);
  assert.match(migration,/review_request_id uuid NOT NULL/);
  assert.match(migration,/UNIQUE\(organization_id,review_request_id\)/);
  assert.match(migration,/SELECT \* INTO v_shift FROM pos_cashier.shifts[\s\S]*FOR UPDATE/);
  assert.match(migration,/pos_shift_second_count_already_reviewed/);
  assert.match(migration,/shift_recovery_review/);
  assert.match(migration,/audit.security_events/);
  assert.match(migration,/pos_cashier_independent_review_requires_forward_recovery/);
  assert.doesNotMatch(migration,/INSERT INTO pos_cashier\.(sales|payment_events|receipts|refund_events)/);
  assert.doesNotMatch(migration,/UPDATE pos_cashier\.(sales|payment_events|receipts|refund_events)/);
  assert.match(fixtures,/second_count_replay_not_idempotent/);
  assert.match(fixtures,/second_count_evidence_edited/);
  assert.match(fixtures,/second_count_fabricated_ledger_entries/);
});

test('unreviewed and disputed recoveries block both new shifts and operator reactivation',()=>{
  assert.match(migration,/pos_shift_independent_review_required/);
  assert.match(migration,/pos_terminal_recovery_requires_second_count/);
  assert.match(migration,/rev.disposition='count_disputed'/);
  assert.match(fixtures,/unreviewed_recovery_allowed_new_shift/);
  assert.match(fixtures,/disputed_cash_count_register_reopened/);
  assert.match(fixtures,/disputed_cash_count_operator_reactivated/);
  assert.match(fixtures,/two_person_count_variance_evidence_invalid/);
  assert.match(fixtures,/reviewed_shift_blocked_regular_cashier/);
});

test('runtime permission requires explicit staged approval; raw database tables remain private',()=>{
  assert.match(grants,/pos_cashier.read_admin_shift_recovery_review_queue/);
  assert.match(grants,/pos_cashier.review_recovered_cashier_shift/);
  assert.match(grants,/staging_approved/);
  assert.match(grants,/has_table_privilege\(m,'pos_cashier.shift_recovery_reviews'/);
  assert.doesNotMatch(grants,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON pos_cashier/);
  assert.match(migration,/REVOKE ALL ON FUNCTION pos_cashier.review_recovered_cashier_shift/);
  assert.match(migration,/ALTER TABLE pos_cashier.shift_recovery_reviews ENABLE ROW LEVEL SECURITY/);
});
