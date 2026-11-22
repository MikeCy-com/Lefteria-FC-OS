import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {adminSectionsFor} from '../src/lib/admin/module-catalog.ts';

const backend=(path)=>readFileSync(new URL('../../backend/'+path,import.meta.url),'utf8');
const frontend=(path)=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const modules=(role,permissions,organizations=['lefteria_fc_academy'])=>
  adminSectionsFor({id:'synthetic',name:'Synthetic',username:'synthetic',role,permissions,
    organizations:organizations.map(id=>({id,name:id}))}).flatMap(s=>s.modules);

test('cashier menu exposes only the assigned POS workspace, never finance or group management',()=>{
  const cashier=modules('cashier',['shop:operate']);
  assert.deepEqual(cashier.map(x=>x.id),['registers']);
  assert.equal(cashier[0].href,'/admin/pos');
  assert.deepEqual(modules('cashier',['shop:operate'],[]),[]);
  assert.deepEqual(modules('cashier',[]),[]);
  assert.ok(!cashier.some(x=>['orders','payabl','products','groups','users','territories'].includes(x.id)));
  assert.match(frontend('routes/admin/pos/+page.svelte'),/data-testid="admin-pos-cashier-only"/);
});
test('cashier cannot access finance, catalog write or refund approval APIs',()=>{
  const api=backend('src/LefteriaFc.Api/Pos/AdminPosCashierEndpoints.cs');
  assert.match(api,/shop:operate/);
  assert.match(api,/managerOnly: true/);
  assert.match(api,/pos_cashier_supervision_required/);
  const roles=backend('src/LefteriaFc.Domain/AdminAuth/AdminPermissions.cs');
  assert.match(roles,/\["cashier"\] = Permissions\("shop:operate"\)/);
  assert.doesNotMatch(roles,/\["cashier"\] = Permissions\("shop:\*"\)/);
});
test('cashier is one organization and one register at DB level; admin can disable it',()=>{
  const sql=backend('src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115950000_PosLeastPrivilegeCashierRole.cs');
  const up=sql.slice(0,sql.indexOf('protected override void Down'));
  assert.match(up,/\(_role='cashier' AND cardinality\(_owners\)<>1\)/);
  assert.match(up,/pos_cashier_single_terminal_required/);
  assert.match(up,/FOR UPDATE OF a FOR SHARE OF g/);
  assert.match(up,/identity\.set_managed_admin_status/);
  assert.match(up,/v_role='cashier' OR club\.admin_football_allowed/);
  assert.match(up,/fl\.is_active AND fc\.is_active/);
  assert.match(sql,/cashier_accounts_require_forward_recovery/);
  const smoke=readFileSync(new URL('../../ops/postgres/pos-site-isolation-checks.sql',import.meta.url),'utf8');
  for (const label of ['cashier_cross_city_register_access','cashier_read_location_finance',
    'cashier_assigned_second_active_register','suspended_cashier_retained_terminal_access',
    'cashier_empty_shift_reconciliation_invalid'])assert.match(smoke,new RegExp(label));
});
