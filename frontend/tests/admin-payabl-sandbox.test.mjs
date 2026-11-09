import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = relative => readFileSync(new URL(relative.startsWith('frontend/') ? '../' + relative.slice(9) : '../../' + relative, import.meta.url),'utf8');
const gateway = read('backend/src/LefteriaFc.Api/Payabl/PayablGateway.cs');
const api = read('backend/src/LefteriaFc.Api/Payabl/PayablEndpoints.cs');
const callback = read('backend/src/LefteriaFc.Api/Payabl/PayablNotification.cs');
const databaseSmoke = read('ops/postgres/payabl-callback-state-checks.sql');
const migration = read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261108320000_PayablPaymentRequests.cs');
const grants = read('ops/postgres/payabl-finance-grants.sql');
const page = read('frontend/src/routes/admin/finance/+page.svelte');
const workspace = read('frontend/src/lib/admin/AdminWorkspace.svelte');
const frame = read('frontend/src/lib/admin/AdminFrame.svelte');

test('payabl is explicitly sandbox only and never accepts payment card information',()=>{
  assert.match(gateway,/Sandbox \{ get; set; \} = true/);
  assert.match(gateway,/!value\.Enabled \|\| \(value\.Sandbox/);
  assert.match(gateway,/https:\/\/sandbox\.payabl\.com\/pay\/payment\/init/);
  assert.match(gateway,/target\.Host is not \("sandbox\.payabl\.com" or "pay4\.sandbox\.payabl\.com"\)/);
  assert.doesNotMatch(api + page,/\b(ccn|cvv|pan|card_number|card_cvv|iban)\s*[:=]/i);
  assert.match(api,/employee_card_payouts = false/);
  assert.match(api,/subscriptions = false/);
  assert.match(api,/live_transactions = false/);
});

test('admin finance reads and writes must recheck session, permission and organization',()=>{
  assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(api,/admin\.CurrentAsync\(cookie, ct\)/);
  assert.match(api,/AdminPermissions\.Has\(session\.User\.Role, write \? "finance:write" : "finance:read"\)/);
  assert.match(api,/session\.User\.Organizations\.Any\(org => org\.Id == owner\)/);
  assert.match(migration,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
  assert.match(migration,/v_role NOT IN \('manager','super_admin'\)/);
  assert.match(migration,/UNIQUE \(organization_id,actor_id,client_request_id\)/);
  assert.match(migration,/INSERT INTO audit\.security_events/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions, 'finance:read'\)/);
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions, 'finance:write'\)/);
  assert.match(page,/'X-Lefteria-Admin':'1'/);
  assert.match(page,/credentials:'same-origin',cache:'no-store'/);
  assert.match(page,/verifiedPayment/);
});

test('signed webhook is bound to an opaque callback nonce and capture transaction',()=>{
  assert.match(gateway,/CryptographicOperations\.FixedTimeEquals\(expected, provided\)/);
  assert.match(gateway,/TimeSpan\.FromMinutes\(15\)/);
  assert.match(api,/PayablNotification\.TryRead\(values, id, settings\.Value\.Secret/);
  assert.match(callback,/PayablSignature\.Notification\(values, secret, now\)/);
  assert.match(migration,/callback_digest varchar\(64\)/);
  assert.match(migration,/v_expected_order<>_order_id/);
  assert.match(migration,/_kind='capture' AND _code=0/);
  assert.match(callback,/paymentId.ToString\("N"\)/);
  assert.match(migration,/ON CONFLICT DO NOTHING/);
  assert.match(migration,/status='review_required'/);
  assert.doesNotMatch(page,/status\s*=\s*'paid'/);
});

test('finance roles get exact functions without private finance table access',()=>{
  assert.match(grants,/REVOKE ALL ON ALL TABLES IN SCHEMA finance/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION finance\.create_admin_payment_request/);
  assert.match(grants,/GRANT EXECUTE ON FUNCTION finance\.apply_payabl_notification/);
  assert.match(grants,/Finance cross-role function leakage/);
  assert.match(frame,/href="\/admin\/finance"/);
  assert.match(workspace,/href="\/admin\/finance"/);
  assert.doesNotMatch(api,/payment_cft|payment_refund|bank_transfer/i);
});

test('public payer return page cannot claim that a browser redirect means payment',()=>{
  const page = read('frontend/src/routes/payments/return/+page.svelte');
  assert.match(api,/\/payments\/return/);
  assert.match(page,/data-testid="payabl-public-return"/);
  assert.match(page,/δεν αποτελεί επιβεβαίωση εξόφλησης/);
  assert.doesNotMatch(page,/fetch\(|\{@html\}|payment\.status|paid = true/i);
});

test('sandbox card-only settlement is fail-closed for unsigned APM states',()=>{
  assert.match(gateway,/\["payment_method"\] = "1"/);
  assert.match(api,/AllowAutoRedirect = false/);
  assert.match(api,/UseCookies = false/);
  assert.match(migration,/status='awaiting_settlement'/);
  assert.match(migration,/_state='' AND v_matches/);
  assert.match(migration,/v_status IN \('ready','awaiting_settlement'\)/);
  assert.match(databaseSmoke,/not_credited_yet/);
  assert.match(databaseSmoke,/Wrong amount incorrectly marked paid/);
  assert.match(callback,/StrictAmount/);
  assert.match(callback,/StrictState/);
});

test('finance reconciliation view displays only bounded provider metadata',()=>{
  assert.match(migration,/'last_provider_event'/);
  assert.match(migration,/FROM finance\.payment_notifications n/);
  assert.match(migration,/WHERE n\.request_id=p\.id/);
  assert.match(page,/verifiedProviderEvent\(row\.last_provider_event\)/);
  assert.match(page,/data-testid="payabl-latest-event"/);
  assert.match(page,/Απαιτείται ανεξάρτητη επιβεβαίωση στο payabl/);
  assert.doesNotMatch(page,/\{@html\}|innerHTML/);
});
