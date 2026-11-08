import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const root = new URL('../../', import.meta.url);
const file = relative => readFileSync(new URL(relative,root),'utf8');
const api=file('backend/src/LefteriaFc.Api/Pos/AdminPosProductEndpoints.cs');
const sql=file('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108113000_PosAdminProductWriter.cs');
const adapter=file('backend/src/LefteriaFc.Infrastructure/Pos/PostgresAdminPosProductWriter.cs');
const grant=file('ops/postgres/web-admin-auth-grants.sql');
const editor=file('frontend/src/routes/admin/pos/+page.svelte');

test('product writer has nested strict JSON, one audited SQL path and no checkout',()=>{
  assert.ok(api.includes('RejectDuplicates(property.Value)'));
  assert.ok(api.includes('JsonUnmappedMemberHandling.Disallow'));
  assert.ok(api.includes('.RequireAuthorization(WebAdminServices.Policy)'));
  assert.ok(api.includes('AdminPermissions.Has(session.User.Role,"shop:write")'));
  assert.ok(sql.includes('PERFORM identity.lock_admin_club_authority(_digest,_owner)'));
  assert.ok(sql.includes('UPDATE pos.products'));
  assert.ok(sql.includes('version=_expected_version'));
  assert.ok(sql.includes('DELETE FROM pos.product_variants'));
  assert.ok(sql.includes('INSERT INTO pos.product_variants'));
  assert.ok(sql.includes('INSERT INTO audit.security_events'));
  assert.ok(sql.includes('IF v_version IS NULL'));
  assert.ok(sql.includes('jsonb_array_length(_variants) NOT BETWEEN 1 AND 16'));
  assert.ok(adapter.includes('NpgsqlDbType.Jsonb'));
  assert.ok(adapter.includes('result.Variants.Count != variants.Count'));
  assert.ok(grant.includes('GRANT EXECUTE ON FUNCTION pos.write_admin_product('));
  assert.doesNotMatch(grant,/GRANT\s+(SELECT|INSERT|UPDATE|DELETE)\s+ON\s+pos\./);
  assert.doesNotMatch(api,/MapDelete\(|MapGet\(|\"\/api\/pos\/(checkout|sales|payments)/i);
});

test('catalog stays distinct from POS operators and checkout; no products editor enabled in UI before verification',()=>{
  assert.match(editor,/Οι αλλαγές προϊόντων, τα ταμεία, οι πωλήσεις και οι πληρωμές παραμένουν ανενεργές/);
  assert.doesNotMatch(editor,/fetch\('\/api\/pos\/checkout|\/api\/pos\/sales/);
});
