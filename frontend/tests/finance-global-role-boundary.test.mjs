import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const repo = path => readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const functions=['create_admin_payment_request','ready_admin_payment_request',
  'read_admin_payment_requests','issue_admin_training_fee',
  'attach_admin_training_fee_payment','read_admin_training_fees',
  'reserve_admin_training_fee_sms','finish_admin_training_fee_sms',
  'record_admin_training_fee_cash','read_admin_invoice_pdf'];
test('global finance migration narrows all ten sensitive functions to accountant or super admin',()=>{
  const file=repo('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115940000_FinanceGlobalRoleBoundary.cs');
  const up=file.slice(0,file.indexOf('protected override void Down'));
  assert.equal(up.match(/v_role NOT IN \('accountant','super_admin'\)/g)?.length,10);
  assert.doesNotMatch(up,/v_role NOT IN \('manager','super_admin'\)/);
  for(const name of functions)assert.match(up,new RegExp('CREATE OR REPLACE FUNCTION finance\\.'+name+'\\('),name);
  assert.match(file,/finance_global_role_requires_reviewed_forward_recovery/);
  assert.match(repo('ops/postgres/finance-global-role-checks.sql'),/pg_get_functiondef/);
});
test('finance APIs and global navigation reject site managers without hiding site reports',()=>{
  for(const file of ['PayablEndpoints.cs','AcademyFeeEndpoints.cs','AcademyInvoiceEndpoints.cs']){
    const api=repo('backend/src/LefteriaFc.Api/Payabl/'+file);
    assert.match(api,/session\.User\.Role is not \("super_admin" or "accountant"\)/,file);
  }
  const finance=repo('frontend/src/routes/admin/finance/+page.svelte');
  assert.match(finance,/\['super_admin','accountant'\]\.includes\(\$auth\.session\.user\.role\)/);
  const modules=repo('frontend/src/lib/admin/module-catalog.ts');
  assert.match(modules,/\['payabl','charges','reports'\]\.includes\(item\.id\)/);
  const site=repo('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115250000_AcademyLocationFinanceAttribution.cs');
  assert.match(site,/club\.admin_football_allowed\(/);
});
