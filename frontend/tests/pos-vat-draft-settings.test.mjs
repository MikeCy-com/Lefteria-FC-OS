import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosVatSettingsEndpoints.cs');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261122150000_PosVatClassificationProposals.cs');
const grants=read('ops/postgres/pos-vat-proposals-staging-grants.sql');
const page=read('frontend/src/routes/admin/settings/pos-vat/+page.svelte');
const provider=read('frontend/src/routes/admin/settings/providers/+page.svelte');
const finance=read('frontend/src/routes/admin/finance/+page.svelte');
const program=read('backend/src/LefteriaFc.Api/Program.cs');

test('separate zero and exempt plus all other Cyprus treatment codes are proposals only',()=>{
  for(const code of ['standard_19','reduced_9','reduced_5','reduced_3','zero_0','exempt']){
    assert.ok(api.includes(code),'api missing '+code);
    assert.ok(migration.includes(code),'db missing '+code);
    assert.ok(page.includes(code),'ui missing '+code);
  }
  assert.match(migration,/status varchar\(16\) NOT NULL DEFAULT 'pending' CHECK\(status='pending'\)/);
  assert.match(migration,/FOREIGN KEY\(organization_id,product_id,variant_id\)/);
  assert.match(migration,/pos\.product_variants\(organization_id,product_id,id\)/);
  assert.match(migration,/IF EXISTS\(SELECT 1 FROM pos_cashier\.vat_classification_proposals\)/);
  assert.match(migration,/pos_vat_proposals_require_forward_recovery/);
  assert.match(migration,/CREATE TRIGGER pos_vat_proposal_immutable/);
  assert.match(migration,/pos\.reject_private_pos_ledger_change\(\)/);
  assert.doesNotMatch(migration,/UPDATE pos_cashier\.(?:sales|receipts|vat_treatments|variant_vat_assignments)/i);
});
test('two layers of scoped authenticated finance permissions with audit and replay safety',()=>{
  assert.match(api,/AdminPermissions.Has\(session.User.Role,"finance:write"\)/);
  assert.match(api,/session.User.Organizations.Any\(o=>o.Id==owner\)/);
  assert.match(migration,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(migration,/identity\.admin_organization_grants g/);
  assert.match(migration,/v_role NOT IN \('super_admin','manager','accountant'\)/);
  assert.match(migration,/pos_vat_proposal_replay_conflict/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.match(migration,/REVOKE ALL ON pos_cashier\.vat_classification_proposals FROM PUBLIC/);
  assert.match(migration,/ALTER TABLE pos_cashier\.vat_classification_proposals ENABLE ROW LEVEL SECURITY/);
  assert.match(migration,/REVOKE ALL ON FUNCTION pos_cashier\.propose_admin_variant_vat/);
  assert.match(migration,/REVOKE ALL ON FUNCTION pos_cashier\.read_admin_vat_proposals/);
  assert.doesNotMatch(migration,/GRANT EXECUTE/i,'migration must never auto-grant access');
});
test('staging SQL grant requires reviewed narrow admin DB role and forbids mobile and raw table rights',()=>{
  assert.match(grants,/^\\set ON_ERROR_STOP on/m);
  assert.match(grants,/:\{\?admin_auth_role\}/);
  assert.match(grants,/admin_auth_role_required/);
  assert.match(grants,/REVOKE|NOT has_function_privilege/);
  assert.match(grants,/has_table_privilege\(a,'pos_cashier\.vat_classification_proposals'/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION pos_cashier\.propose_admin_variant_vat/);
  assert.doesNotMatch(grants,/GRANT .*pos_cashier\.(?:sales|receipts|refund_events|payment_events)/i);
});
test('admin UI is a real scoped form with pending-only receipt/payabl/fiscal status',()=>{
  assert.match(program,/app\.MapAdminPosVatSettings\(\)/);
  assert.match(provider,/data-testid="admin-provider-vat-settings"/);
  assert.match(finance,/data-testid="finance-vat-settings-link"/);
  assert.match(page,/data-testid="admin-pos-vat-settings"/);
  assert.match(page,/data-testid="vat-policy-guard"/);
  assert.match(page,/checkout_enabled===false/);
  assert.match(page,/approval_available===false/);
  assert.match(page,/crypto\.randomUUID\(\)/);
  assert.match(page,/method:'POST',credentials:'same-origin'/);
  assert.match(page,/\/api\/admin\/pos\/catalog/);
  assert.match(page,/\/api\/admin\/finance\/pos-vat\/proposals/);
  assert.match(page,/request_id:requestId/);
  assert.ok(compile(page,{filename:'admin/settings/pos-vat/+page.svelte',generate:'server'}).js.code.length>100);
});
