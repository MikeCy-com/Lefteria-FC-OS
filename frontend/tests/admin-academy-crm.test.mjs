import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read=(file)=>readFileSync(new URL('../../'+file,import.meta.url),'utf8');
const api=read('backend/src/LefteriaFc.Api/Crm/AcademyLeadAdminEndpoints.cs');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109120000_AcademyLeadAdminInbox.cs');
const grants=read('ops/postgres/academy-crm-admin-grants.sql');
const checks=read('ops/postgres/academy-crm-inbox-checks.sql');
const page=read('frontend/src/routes/admin/leads/+page.svelte');
const catalog=read('frontend/src/lib/admin/module-catalog.ts');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const ci=read('.github/workflows/rewrite-build.yml');

test('CRM only reads Academy leads under live manager permission and exact owner',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/organization!="lefteria_fc_academy"/);
  assert.match(api,/Count!=1/);
  assert.match(api,/session\.User\.Role is not \("manager" or "super_admin"\)/);
  assert.match(api,/marketing:read/);
  assert.match(migration,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(migration,/g\.organization_id=_owner/);
  assert.match(migration,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(migration,/l\.organization_id=_owner/);
  assert.match(migration,/LIMIT _limit OFFSET _offset/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION marketing\.read_admin_academy_leads/);
  assert.match(grants,/has_any_column_privilege\(v_admin,c\.oid/);
  assert.match(grants,/has_function_privilege\(v_mobile,v_oid/);
});

test('CRM cannot auto-enroll, SMS, mark paid or fabricate trial bookings',()=>{
  assert.match(migration,/v_current<>_expected/);
  assert.match(migration,/FOR UPDATE/);
  assert.match(migration,/INSERT INTO marketing\.lead_review_events/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.match(migration,/IF v_current=_next THEN/);
  assert.match(migration,/NOT \(\(_expected='new' AND _next IN \('contacted','lost'\)\)/);
  assert.doesNotMatch(migration,/UPDATE club\.players|INSERT INTO club\.players/);
  assert.doesNotMatch(api,/Twilio|SendAsync\(|payment_method|force_success/i);
  assert.match(checks,/CRM fabricated player registration/);
  assert.match(checks,/CRM duplicate followup was not idempotent/);
});

test('private admin route validates server results and does not expose leads to other roles',()=>{
  assert.match(page,/data-testid="admin-academy-crm"/);
  assert.match(page,/\['manager','super_admin'\]/);
  assert.match(page,/organization_id:'lefteria_fc_academy'/);
  assert.match(page,/validPage\(result,filter,pageOffset\)/);
  assert.match(page,/validLead\(item\)/);
  assert.match(page,/data-testid="admin-academy-crm-denied"/);
  assert.match(page,/X-Lefteria-Admin/);
  assert.doesNotMatch(page,/@html|innerHTML|localStorage\./);
  assert.match(catalog,/href: '\/admin\/leads'/);
  assert.match(frame,/href="\/admin\/leads"/);
  assert.match(program,/app\.MapAcademyLeadAdmin\(\)/);
});

test('CI initializes manager grants and exercises disposable contact lifecycle',()=>{
  assert.match(ci,/academy-crm-admin-grants\.sql/);
  assert.match(ci,/academy-crm-inbox-checks\.sql/);
  assert.match(checks,/ROLLBACK;/);
  assert.match(checks,/Cross-organization CRM read succeeded/);
  assert.match(checks,/Unbounded CRM read succeeded/);
  assert.match(checks,/Global admin security audit missing/);
});
