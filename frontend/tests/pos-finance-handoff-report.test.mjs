import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const db=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110165000_PosFinanceHandoffRead.cs');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
const milestone=read('docs/implementation/unified-pos-payments-milestone.md');
const page=read('frontend/src/routes/admin/pos/+page.svelte');
const panel=read('frontend/src/lib/admin/AdminPosFinanceHandoffPanel.svelte');
test('finance handoff is read-only and current-manager scoped',()=>{
  assert.match(db,/identity.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(db,/admin_organization_grants g/);
  assert.match(db,/s.revoked_at IS NULL/);
  assert.match(db,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(db,/REVOKE ALL ON FUNCTION pos.read_admin_finance_handoff/);
  assert.doesNotMatch(db,/INSERT INTO|UPDATE (?:finance|club|academy)\.|GRANT EXECUTE/);
});
test('finance handoff correctly separates receipts from verified refunds',()=>{
  assert.match(db,/event_kind='sale_receipted'/);
  assert.match(db,/event_kind='refund_verified'/);
  assert.match(db,/sum\(amount_cents\)/);
  assert.match(db,/v_gross-v_refunded/);
  assert.match(db,/awaiting_verified_finance_consumer/);
  assert.match(db,/'journal_entries_confirmed',0/);
  assert.match(db,/'finance_posting_active',false/);
});
test('HTTP route enforces both finance read and POS management',()=>{
  assert.match(api,/ReadAdminPosFinanceHandoff/);
  assert.match(api,/Authority\(auth, organization_id, context, ct\)/);
  assert.match(api,/AdminPermissions.Has\(session.User.Role, "finance:read"\)/);
  assert.match(api,/pos.read_admin_finance_handoff/);
  assert.doesNotMatch(api,/MapPost\("\/finance-handoff"/);
});
test('milestone requires all four phases and no unverified rollout',()=>{
  for(const p of ['Payabl full sandbox lifecycle','Operational POS','App, website & finance integration','Testing & release control'])
    assert.ok(milestone.includes(p));
  assert.match(milestone,/All phases are conjunctive/);
  assert.match(milestone,/Never deploy to production|never write to/);
});

test('finance handoff UI refuses false posted states and stays read-only',()=>{
  assert.match(page,/AdminPosFinanceHandoffPanel/);
  assert.match(page,/hasAdminPermission\(\$auth.session.user.permissions,'finance:read'\)/);
  assert.match(panel,/posting_status==='awaiting_verified_finance_consumer'/);
  assert.match(panel,/finance_posting_active===false/);
  assert.match(panel,/journal_entries_confirmed===0/);
  assert.match(panel,/item.net_event_cents/);
  assert.match(panel,/credentials:'same-origin',cache:'no-store'/);
  assert.doesNotMatch(panel,/method:'POST'|method:'PUT'|localStorage|sessionStorage/);
});