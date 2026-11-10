import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261110180000_PosAdminLocationOperations.cs');
const api=read('backend/src/LefteriaFc.Api/Pos/AdminPosLocationEndpoints.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const ui=read('frontend/src/lib/admin/AdminPosLocationsPanel.svelte');
const page=read('frontend/src/routes/admin/pos/+page.svelte');
const grants=read('ops/postgres/pos-location-admin-grants.sql');
test('exact owner + manager role + session rechecked in both HTTP and PostgreSQL',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/write\?"shop:write":"shop:read"/);
  assert.match(api,/session\.User\.Organizations\.Any/);
  assert.match(api,/TokenDigest\(cookie\)/);
  assert.match(api,/CacheControl="private, no-store"/);
  assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(sql,/s\.revoked_at IS NULL/);
  assert.match(sql,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(sql,/INSERT INTO audit\.security_events/);
  assert.match(program,/app\.MapAdminPosLocations\(\)/);
});
test('location cannot be created without an active owner-scoped city',()=>{
  assert.match(sql,/WHERE organization_id=_owner AND id=_city AND active/);
  assert.match(sql,/pos_location_limit_reached/);
  assert.match(sql,/pos_city_limit_reached/);
  assert.match(sql,/REVOKE ALL ON FUNCTION pos\.require_admin_location_owner/);
  assert.match(sql,/REVOKE ALL ON FUNCTION pos\.create_admin_location/);
  assert.doesNotMatch(api,/checkout|sale_payment|operator_session/);
});
test('grants expose functions only to separate admin-auth runtime role',()=>{
  assert.match(grants,/GRANT EXECUTE ON FUNCTION pos\.read_admin_locations/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION pos\.create_admin_city/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION pos\.create_admin_location/);
  assert.match(grants,/pos_location_function_grant_mismatch/);
  assert.match(grants,/private_pos_location_table_exposed/);
  assert.doesNotMatch(grants,/GRANT SELECT ON|GRANT INSERT ON/);
});
test('POS administration uses real scoped directory and forms, no fake checkout',()=>{
  assert.match(page,/AdminPosLocationsPanel/);
  assert.match(ui,/data-testid="admin-pos-locations"/);
  assert.match(ui,/validId/);
  assert.match(ui,/verified\(raw,currentOwner\)/);
  assert.match(ui,/organization_id:selectedOwner/);
  assert.match(ui,/X-Lefteria-Admin/);
  assert.match(ui,/canWrite/);
  assert.doesNotMatch(ui,/payment_method|cash_received|charge_card|checkout/);
});
