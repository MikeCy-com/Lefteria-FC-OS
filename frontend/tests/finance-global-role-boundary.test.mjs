import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const fn=['create_admin_payment_request','ready_admin_payment_request','read_admin_payment_requests',
  'issue_admin_training_fee','attach_admin_training_fee_payment','read_admin_training_fees',
  'reserve_admin_training_fee_sms','finish_admin_training_fee_sms',
  'record_admin_training_fee_cash','read_admin_invoice_pdf'];

test('all legacy finance SQL functions are Super Admin only and rollback cannot widen access',()=>{
  const src=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115940000_FinanceGlobalRoleBoundary.cs');
  const up=src.slice(0,src.indexOf('protected override void Down'));
  assert.equal((up.match(/v_role<>'super_admin'/g)||[]).length,10);
  assert.doesNotMatch(up,/v_role NOT IN \('accountant','super_admin'\)|v_role NOT IN \('manager','super_admin'\)/);
  for(const name of fn)assert.match(up,new RegExp('CREATE OR REPLACE FUNCTION finance\\.'+name+'\\('),name);
  assert.match(src,/throw new NotSupportedException\("finance_global_role_requires_reviewed_forward_recovery"\)/);
  const sql=read('ops/postgres/finance-global-role-checks.sql');
  assert.match(sql,/pg_get_functiondef/);
  assert.match(sql,/v_role<>''super_admin''/);
});
test('legacy Payabl APIs deny accountants and managers but the separate accountant overview remains',()=>{
  for(const file of ['PayablEndpoints.cs','AcademyFeeEndpoints.cs','AcademyInvoiceEndpoints.cs']){
    const src=read('backend/src/LefteriaFc.Api/Payabl/'+file);
    assert.match(src,/session\.User\.Role\s*!=\s*"super_admin"/,file);
  }
  const finance=read('frontend/src/routes/admin/finance/+page.svelte');
  assert.match(finance,/providerAccess = \$derived\(\$auth\.session\?\.user\.role === 'super_admin'\)/);
  assert.match(finance,/orgWideAccess/);
  const catalog=read('frontend/src/lib/admin/module-catalog.ts');
  assert.match(catalog,/\['payabl','charges','reports'\]\.includes\(item\.id\)/);
  const site=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115250000_AcademyLocationFinanceAttribution.cs');
  assert.match(site,/club\.admin_football_allowed\(/);
});
