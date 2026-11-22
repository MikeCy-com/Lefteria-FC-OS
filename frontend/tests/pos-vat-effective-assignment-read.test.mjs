import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261122152000_PosVatApprovedAssignmentRead.cs');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosVatSettingsEndpoints.cs');
const grants=read('ops/postgres/pos-vat-proposals-staging-grants.sql');
const sql=read('ops/postgres/pos-vat-proposal-checks.sql');
const page=read('frontend/src/routes/admin/settings/pos-vat/+page.svelte');

test('reviewed variant VAT read is tenant scoped, latest authorization and private',()=>{
  assert.match(migration,/CREATE FUNCTION pos_cashier\.read_admin_vat_assignments\(_digest text,_owner text\)/);
  assert.match(migration,/SECURITY DEFINER/);
  assert.match(migration,/SET search_path=pg_catalog,pg_temp/);
  assert.match(migration,/s\.token_digest=_digest AND s\.revoked_at IS NULL/);
  assert.match(migration,/s\.expires_at>clock_timestamp\(\)/);
  assert.match(migration,/g\.organization_id=_owner/);
  assert.match(migration,/v_role NOT IN \('super_admin','accountant'\)/);
  assert.match(migration,/WHERE organization_id=_owner/);
  assert.match(migration,/WHERE t\.organization_id=_owner/);
  assert.match(migration,/REVOKE ALL ON FUNCTION pos_cashier\.read_admin_vat_assignments\(text,text\) FROM PUBLIC/);
  assert.doesNotMatch(migration,/(?:INSERT INTO|UPDATE|DELETE FROM) pos_cashier\.(?:sales|payment_events|vat_treatments|variant_vat_assignments)/);
});
test('effective VAT state stays false for checkout and fiscal approval',()=>{
  assert.match(migration,/'checkout_enabled',false/);
  assert.match(migration,/'fiscal_policies_approved',false/);
  for(const field of ['treatment_revision','basis_points','effective_from','effective_until','authority_reference','enabled'])
    assert.ok(migration.includes("'"+field+"'"),field);
  assert.match(api,/group\.MapGet\("\/assignments"/);
  assert.match(api,/var cookie=await Verify\(admin,context,organization_id,ct\)/);
  assert.match(api,/pos_cashier\.read_admin_vat_assignments\(@digest,@owner\)/);
  assert.doesNotMatch(migration,/GRANT EXECUTE/);
});
test('staging grants are opt-in and never allow mobile or raw-table access',()=>{
  assert.match(grants,/pos_cashier\.read_admin_vat_assignments\(text,text\)/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION pos_cashier\.read_admin_vat_assignments/);
  assert.match(grants,/has_function_privilege\(m,f,'EXECUTE'\)/);
  assert.match(sql,/unauthenticated_vat_assignments_allowed/);
  assert.match(sql,/pos_cashier\.read_admin_vat_assignments/);
});
test('backoffice displays reviewed tax revisions and never claims payments enabled',()=>{
  assert.match(page,/data-testid="vat-reviewed-assignments"/);
  assert.match(page,/\/api\/admin\/finance\/pos-vat\/assignments/);
  assert.match(page,/value\.organization_id!==current/);
  assert.match(page,/value\.checkout_enabled!==false/);
  assert.match(page,/value\.fiscal_policies_approved!==false/);
  assert.match(page,/owner===current/);
  assert.ok(compile(page,{filename:'pos-vat/+page.svelte',generate:'server'}).js.code.length>100);
});
