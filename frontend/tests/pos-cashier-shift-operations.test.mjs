import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const load=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const sql=load('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110153000_PosCashierShiftOperations.cs');
const api=load('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
const main=load('backend/src/LefteriaFc.Api/Program.cs');
const grants=load('ops/postgres/pos-cashier-shifts-admin-grants.sql');
const ui=load('frontend/src/lib/admin/AdminPosCashierShiftsPanel.svelte');
const parent=load('frontend/src/routes/admin/pos/+page.svelte');

test('shift writes require current dedicated assigned cashier, register, location and grant',()=>{
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/s\.revoked_at IS NULL/);
  assert.match(sql,/s\.expires_at>clock_timestamp\(\)/);
  assert.match(sql,/admin_organization_grants g/);
  assert.match(sql,/ca\.admin_account_id=a\.id AND ca\.is_active/);
  assert.match(sql,/r\.organization_id=_owner AND r\.id=_register AND r\.is_active/);
  assert.match(sql,/l\.organization_id=_owner AND l\.id=r\.location_id AND l\.is_active/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
});
test('cashier shift opens are idempotent and only one shift may be open',()=>{
  assert.match(sql,/open_request_id uuid/);
  assert.match(sql,/CREATE UNIQUE INDEX uq_pos_shift_open_request/);
  assert.match(sql,/open_request_id=_request FOR UPDATE/);
  assert.match(sql,/cashier_open_replay_mismatch/);
  assert.match(sql,/INSERT INTO pos\.shifts/);
  assert.match(sql,/shift_open/);
});
test('shift close is cashier-owned, cash reconciled and cannot silently discard pending transactions',()=>{
  assert.match(sql,/v_actor<>v_shift\.cashier_id/);
  assert.match(sql,/pos\.sales WHERE organization_id=_owner/);
  assert.match(sql,/status NOT IN \('paid','voided'\)/);
  assert.match(sql,/f\.status='pending'/);
  assert.match(sql,/p\.status='verified' AND p\.payment_kind='cash'/);
  assert.match(sql,/f\.status='verified' AND f\.refund_kind='cash'/);
  assert.match(sql,/v_expected:=v_shift\.opening_cash_cents\+v_cash_in-v_cash_out/);
  assert.match(sql,/variance_cents=_counted-v_expected/);
  assert.match(sql,/cashier_shift_already_closed/);
  assert.match(sql,/close_request_id=_request/);
  assert.match(sql,/INSERT INTO pos\.transaction_audit/);
});
test('HTTP endpoints use verified current web admin role and never enable payment checkout',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role, "shop:write"\)/);
  assert.match(api,/session\.User\.Organizations\.Any/);
  assert.match(api,/CacheControl = "private, no-store"/);
  assert.match(api,/pos\.read_admin_cashier_registers/);
  assert.match(api,/pos\.open_admin_cashier_shift/);
  assert.match(api,/pos\.close_admin_cashier_shift/);
  assert.match(api,/secrets\.TokenDigest\(cookie\)/);
  assert.match(main,/app\.MapAdminPosCashier\(\)/);
  assert.doesNotMatch(api,/capture_card|execute_refund|receipt\.issue/);
});
test('manual narrow database grants do not expose raw financial records to other roles',()=>{
  assert.match(sql,/REVOKE ALL ON FUNCTION pos\.open_admin_cashier_shift/);
  assert.match(sql,/REVOKE ALL ON FUNCTION pos\.close_admin_cashier_shift/);
  assert.match(grants,/pos_cashier_definer_unhardened/);
  assert.match(grants,/has_function_privilege\(m,f,'EXECUTE'\)/);
  assert.match(grants,/pos_cashier_raw_table_exposed/);
  assert.match(grants,/pos\.current_cashier_authority\(text,text,text\)/);
  assert.doesNotMatch(grants,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON/);
});
test('admin panel only exposes selected register and cash shift actions',()=>{
  assert.match(parent,/AdminPosCashierShiftsPanel/);
  assert.match(parent,/canEdit && \$auth\.session && \['manager','super_admin'\]/);
  assert.match(ui,/validRegister/);
  assert.match(ui,/verified\(data,currentOwner\)/);
  assert.match(ui,/response\.status===401/);
  assert.match(ui,/response\.status===403/);
  assert.match(ui,/confirmedClose/);
  assert.match(ui,/money\(counted\)/);
  assert.match(ui,/X-Lefteria-Admin/);
  assert.doesNotMatch(ui,/localStorage|sessionStorage|capturePayment|completeSale/);
});
