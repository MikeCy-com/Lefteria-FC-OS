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
  assert.match(callback,/id.ToString\("N"\)\[\.\.27\]/);
  assert.match(migration,/LFC\[0-9a-f\]\{27\}/);
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

const feeMigration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109100000_AcademyTrainingFeeBilling.cs');
const feeEndpoints=read('backend/src/LefteriaFc.Api/Payabl/AcademyFeeEndpoints.cs');
const smsAdapter=read('backend/src/LefteriaFc.Api/Payabl/TwilioPaymentSms.cs');
const feeAdmin=read('frontend/src/lib/admin/AdminTrainingFees.svelte');
const feeParent=read('frontend/src/lib/mobile/components/MobileTrainingFees.svelte');
const feePublicPolicy=read('frontend/src/lib/mobile/finance.ts');
const monthTests=read('ops/postgres/payabl-training-fee-checks.sql');

test('only manually selected academy months are billed, never implicit twelve months',()=>{
  assert.match(feeMigration,/UNIQUE \(organization_id,player_id,fee_month\)/);
  assert.match(feeMigration,/fee_month date NOT NULL CHECK \(EXTRACT\(day FROM fee_month\)=1\)/);
  assert.match(feeMigration,/finance\.issue_admin_training_fee/);
  assert.match(feeMigration,/ON CONFLICT \(organization_id,player_id,fee_month\) DO NOTHING/);
  assert.match(feeMigration,/identity\.canonical_contact_bindings b/);
  assert.match(monthTests,/Training-fee issue generated unrequested months/);
  assert.match(monthTests,/Card capture changed independently paid cash invoice/);
  assert.match(monthTests,/Manager cash receipt was not recorded/);
  assert.match(monthTests,/Duplicate cash receipt created/);
  assert.match(feeAdmin,/type="month"/);
  assert.doesNotMatch(feeMigration,/generate_series|interval '12 months'|recurring_invoice_batch/i);
});

test('parent can see only linked child fees and cannot mark them paid from app',()=>{
  assert.match(feeEndpoints,/session\.Role!=MobileRole\.Parent/);
  assert.match(feeMigration,/l\.identity_id=_identity/);
  assert.match(feeMigration,/b\.normalized_phone=_phone/);
  assert.match(feeMigration,/f\.payer_identity_id=_identity/);
  assert.match(feeMigration,/finance\.mark_training_fee_from_capture/);
  assert.match(feeMigration,/IF NEW\.status='paid' AND OLD\.status IS DISTINCT FROM NEW\.status/);
  assert.match(feeParent,/data-testid="parent-fee-click-to-pay"/);
  assert.match(feePublicPolicy,/payablHostedLink/);
  assert.match(feeParent,/Το άνοιγμα της σελίδας πληρωμής δεν θεωρείται εξόφληση/);
  assert.doesNotMatch(feeParent,/\/api\/payabl\/webhooks|status\s*=\s*'paid'/);
});

test('monthly preferred day and saved-card consent never create automatic charges',()=>{
  assert.match(feeMigration,/preferred_day BETWEEN 1 AND 31/);
  assert.match(feeMigration,/automatic_collection_enabled=false/);
  assert.match(feeMigration,/saved_card_available',false/);
  assert.match(feeMigration,/LEAST\(v_day,EXTRACT\(day FROM/);
  assert.match(feeParent,/bind:checked=\{saveCard\}/);
  assert.match(feeParent,/bind:checked=\{automatic\}/);
  assert.match(feeParent,/αυτόματες χρεώσεις/);
  assert.match(monthTests,/Unapproved automatic recurring payment activated/);
});

test('Twilio SMS sends only ready linked fee to verified guardian; delivery never assumed',()=>{
  assert.match(smsAdapter,/!options\.Enabled \|\|/);
  assert.match(smsAdapter,/MessagingServiceSid/);
  assert.match(smsAdapter,/sandbox\.payabl\.com/);
  assert.match(smsAdapter,/api\.twilio\.com\/2010-04-01/);
  assert.match(feeMigration,/finance\.reserve_admin_training_fee_sms/);
  assert.match(feeMigration,/finance\.finish_admin_training_fee_sms/);
  assert.match(feeMigration,/v_payment\.status<>'ready'/);
  assert.match(feeMigration,/v_fee\.payer_identity_id/);
  assert.match(feeEndpoints,/delivery_confirmed=false/);
  assert.match(feeAdmin,/Αποστολή συνδέσμου με Twilio SMS/);
  assert.match(feeAdmin,/SMS αποδεκτό από Twilio · όχι επιβεβαιωμένη παράδοση/);
  assert.doesNotMatch(feeAdmin,/SendAsync\(|fetch\('https:\/\/api\.twilio\.com'/);
});

const invoiceMigration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261109110000_AcademyInvoiceParity.cs');
const invoiceEndpoints=read('backend/src/LefteriaFc.Api/Payabl/AcademyInvoiceEndpoints.cs');
const pdfRenderer=read('backend/src/LefteriaFc.Api/Payabl/AcademyInvoicePdf.cs');
const mobileController=read('frontend/src/lib/mobile/controller.ts');
const adminPdfTests=read('backend/tests/LefteriaFc.Identity.IntegrationTests/PayablAdapterChecks.cs');

test('source-matched invoice PDF reuses original A4 black and gold geometry',()=>{
  assert.match(pdfRenderer,/private const float Mm = 72f \/ 25\.4f/);
  assert.match(pdfRenderer,/const float Width = 210f \* Mm, Height = 297f \* Mm/);
  assert.match(pdfRenderer,/private static readonly SKColor Gold = Color\(245,166,35\)/);
  assert.match(pdfRenderer,/Rect\(c,0,0,Width,U\(42\),Black\)/);
  assert.match(pdfRenderer,/Section\(c,"PARENT \/ GUARDIAN DETAILS"/);
  assert.match(pdfRenderer,/Section\(c,"PLAYER DETAILS"/);
  assert.match(pdfRenderer,/Section\(c,"PAYMENT METHOD"/);
  assert.match(pdfRenderer,/Footer\(c,regular,bold,logo\)/);
  assert.match(pdfRenderer,/SKDocument\.CreatePdf\(stream\)/);
  assert.match(adminPdfTests,/branded invoice PDF/);
});

test('cash and card invoice states originate from server-side audited finance data',()=>{
  assert.match(invoiceMigration,/CREATE TABLE finance\.training_fee_cash_receipts/);
  assert.match(invoiceMigration,/v_fee\.payment_request_id IS NOT NULL/);
  assert.match(monthTests,/Second cash receipt accepted/);
  assert.match(invoiceMigration,/finance\.record_admin_training_fee_cash/);
  assert.match(invoiceMigration,/f\.invoice_due_date/);
  assert.match(invoiceMigration,/WHEN cr\.id IS NOT NULL THEN 'cash'/);
  assert.match(invoiceMigration,/WHEN r\.status='paid' THEN 'card'/);
  assert.match(invoiceMigration,/l\.identity_id=_identity/);
  assert.match(invoiceEndpoints,/AcademyInvoicePdf\.Parse\(raw\)/);
  assert.match(invoiceEndpoints,/AcademyInvoicePdf\.Render\(document,logo,club\)/);
  assert.match(pdfRenderer,/PaymentMethod is not \("cash" or "card" or "unpaid"\)/);
  assert.match(pdfRenderer,/is "cash" or "card"/);
});

test('parent and admin download individually or together without public PDF links',()=>{
  assert.match(invoiceEndpoints,/MapGet\("\/invoices\/multi\.pdf"/);
  assert.match(invoiceEndpoints,/MapGet\("\/multi\.pdf"/);
  assert.match(invoiceEndpoints,/RequireAuthorization\(IdentityServices\.Policy\)/);
  assert.match(invoiceEndpoints,/RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(invoiceMigration,/cardinality\(_ids\)/);
  assert.match(mobileController,/async downloadTrainingInvoice\(ids:string\[\]\):Promise<Blob>/);
  assert.match(feeParent,/data-testid="parent-download-invoice"/);
  assert.match(feeParent,/data-testid="parent-download-family-invoice"/);
  assert.match(feeAdmin,/data-testid="admin-download-invoice"/);
  assert.match(feeAdmin,/data-testid="admin-download-multi-invoice"/);
  assert.match(feeAdmin,/data-testid="admin-record-cash"/);
  assert.doesNotMatch(feeParent,/innerHTML|\{@html\}/);
});

test('immutable due date follows issued month rather than later preference edits',()=>{
  assert.match(feeMigration,/CREATE TRIGGER trg_training_fee_invoice_due/);
  assert.match(feeMigration,/invoice_due_date_is_server_issued/);
  assert.match(monthTests,/Changing preference rewrote issued invoice due date/);
  assert.match(monthTests,/February due date did not clamp to month end/);
});
