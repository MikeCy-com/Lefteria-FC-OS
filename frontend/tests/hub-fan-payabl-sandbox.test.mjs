import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const migration=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113193000_HubFanPayablSandboxIntents.cs');
const endpoints=read('backend/src/LefteriaFc.Api/Payabl/HubFanPayablCheckoutEndpoints.cs');
const existing=read('backend/src/LefteriaFc.Api/Payabl/PayablGateway.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const permissions=read('ops/postgres/identity-app-permissions-check.sql');
const program=read('backend/src/LefteriaFc.Api/Program.cs');

test('fan Payabl initial checkout requires verified fan identity and server-side price',()=>{
 assert.match(migration,/_role IS DISTINCT FROM 'fan'/);
 assert.match(migration,/i\.id=_identity AND i\.normalized_phone=_phone/);
 assert.match(migration,/i\.role='fan' AND i\.is_active/);
 assert.match(migration,/UNIQUE\(subscriber_identity_id,organization_id,client_request_id\)/);
 assert.match(migration,/ux_hub_fan_checkout_open/);
 assert.match(migration,/consent_at timestamptz NOT NULL/);
 assert.match(endpoints,/actor\.Role!=MobileRole\.Fan/);
 assert.match(endpoints,/!body\.AcceptedTerms/);
 assert.match(endpoints,/var amount=fanOptions\.Price\(body\.Audience\)/);
 assert.doesNotMatch(endpoints,/body\.AmountCents|body\.PriceCents/);
 assert.match(endpoints,/body\.ClientRequestId/);
 assert.match(endpoints,/if\(!current\.Created\)/);
 assert.match(existing,/https:\/\/sandbox\.payabl\.com\/pay\/payment\/init/);
});

test('only sandbox staging checkout initialization uses verified hosted gateway',()=>{
 assert.match(endpoints,/environment\.IsStaging\(\)/);
 assert.match(endpoints,/!fanOptions\.Enabled/);
 assert.match(endpoints,/!payabl\.Enabled/);
 assert.match(endpoints,/!gateway\.Available/);
 assert.match(endpoints,/PayablNotification\.OrderId\(id\)/);
 assert.match(endpoints,/PayablSignature\.CallbackDigest\(nonce\)/);
 assert.match(endpoints,/secrets\.NewToken\(\)/);
 assert.match(endpoints,/PayablCheckoutRequest\(/);
 assert.match(endpoints,/gateway\.InitializeAsync/);
 assert.match(endpoints,/returnUrl|origin\+"\/payments\/return"/);
 assert.match(endpoints,/"ready",hosted\.CheckoutUrl/);
 assert.match(program,/AddOptions<HubFanPayablOptions>/);
 assert.match(program,/MapHubFanPayablCheckout\(\)/);
});

test('signed provider callback is not proof of entitlement, only reconciliation evidence',()=>{
 assert.match(endpoints,/PayablNotification\.TryRead\(fields,id,payabl\.Value\.Secret/);
 assert.match(endpoints,/nonce\.Length!=43/);
 assert.match(endpoints,/buffer\.Length\+read>4096/);
 assert.match(endpoints,/PayablGateway\.ParseFields/);
 assert.match(endpoints,/record_fan_payabl_checkout_notice/);
 assert.match(migration,/callback_digest=_nonce_digest/);
 assert.match(migration,/order_id=_order FOR UPDATE/);
 assert.match(migration,/IF _amount IS NOT NULL AND _amount<>v_row\.amount_cents/);
 assert.match(migration,/ON CONFLICT DO NOTHING/);
 assert.match(migration,/status=CASE WHEN _code=0/);
 assert.match(migration,/_kind='capture' THEN/);
 assert.match(migration,/review_required/);
 assert.doesNotMatch(migration,/UPDATE hub\.fan_subscriptions|INSERT INTO hub\.fan_subscriptions|status='active'|verified_at=/);
 assert.doesNotMatch(endpoints,/activate.*subscription|paid.*true/i);
});

test('Payabl storage and scheduler remain private, and all attempted grants are audited',()=>{
 assert.match(migration,/ALTER TABLE hub\.fan_payabl_checkout_intents ENABLE ROW LEVEL SECURITY/);
 assert.match(migration,/ALTER TABLE hub\.fan_payabl_checkout_notices ENABLE ROW LEVEL SECURITY/);
 assert.match(migration,/Refusing to destroy recorded fan checkout history/);
 assert.match(grants,/REVOKE ALL ON TABLE hub\.fan_payabl_checkout_intents/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.begin_mobile_fan_payabl_checkout/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.ready_mobile_fan_payabl_checkout/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.record_fan_payabl_checkout_notice/);
 assert.match(permissions,/Runtime weekly HUB\/Payabl role boundary violated/);
 assert.match(permissions,/Unverified fan bypassed paid weekly enrollment/);
 assert.doesNotMatch(grants,/GRANT (SELECT|INSERT|UPDATE|DELETE) ON hub\.fan_subscriptions/);
 assert.match(endpoints,/RequireAuthorization\(IdentityServices\.Policy\)/);
});
