import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261115920000_HubFanAdminOverview.cs');
const endpoint=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const permissions=read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary=read('ops/postgres/admin-hub-fan-overview-boundary.sql');
const app=read('frontend/src/routes/admin/hub/subscriptions/+page.svelte');
const dto=read('frontend/src/lib/admin/hub-fan-overview.ts');
const catalog=read('frontend/src/lib/admin/module-catalog.ts');
const edge=read('deploy/preview/generate-edge.mjs');
const frame=read('frontend/src/lib/admin/AdminFrame.svelte');

test('fan overview SQL is Super Admin only, lock-verified and exact organization-scoped',()=>{
  assert.match(sql,/hub\.read_admin_fan_subscription_overview\(_digest text,_owner text\)/);
  assert.match(sql,/SECURITY DEFINER/);
  assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
  assert.match(sql,/_digest !~ '\^\[0-9a-f\]\{64\}\$'/);
  assert.match(sql,/s\.token_digest=_digest AND s\.revoked_at IS NULL/);
  assert.match(sql,/s\.expires_at>clock_timestamp\(\)/);
  assert.match(sql,/g\.admin_account_id=a\.id AND g\.organization_id=_owner/);
  assert.match(sql,/FOR SHARE OF s,a,g/);
  assert.match(sql,/v_role<>'super_admin'/);
  assert.match(sql,/WHERE p\.organization_id=_owner/);
  assert.match(sql,/FROM hub\.fan_subscriptions s WHERE s\.organization_id=_owner/);
  assert.match(sql,/WHERE s\.organization_id=_owner/);
  assert.doesNotMatch(sql,/'subscriber_id'|'subscriber_identity_id'|'phone'|'payer_email'|'checkout_url'|'provider_reference'|'payment_request_id'/);
  assert.match(sql,/DROP FUNCTION hub\.read_admin_fan_subscription_overview/);
});

test('verified count is not a raw active flag or an unsigned checkout return',()=>{
  assert.match(sql,/AND i\.role='fan' AND i\.is_active/);
  assert.match(sql,/JOIN hub\.fan_checkout_links link ON link\.subscription_id=s\.id/);
  assert.match(sql,/JOIN finance\.payment_requests p ON p\.id=link\.payment_request_id/);
  assert.match(sql,/p\.status='paid' AND p\.captured_transaction_id IS NOT NULL/);
  assert.match(sql,/p\.captured_transaction_id=s\.provider_reference/);
  assert.match(sql,/p\.actor_id=s\.subscriber_identity_id/);
  assert.match(sql,/p\.purpose='fan_hub_'\|\|link\.track/);
  assert.match(sql,/s\.expires_at>clock_timestamp\(\)/);
  assert.match(sql,/NOT EXISTS \(/);
  assert.match(sql,/n\.event_type IN \('refund','reversal'\)/);
  assert.match(sql,/'verified_active',v_verified/);
  assert.doesNotMatch(sql,/UPDATE hub\.fan_subscriptions|INSERT INTO hub\.fan_subscriptions|DELETE FROM hub\.fan_subscriptions/);
});

test('private endpoint independently checks session, finance permission and exact Super Admin organization',()=>{
  assert.match(endpoint,/MapGet\("\/api\/admin\/hub\/fan-subscriptions\/overview"/);
  assert.match(endpoint,/\.RequireAuthorization\(WebAdminServices\.Policy\)/);
  assert.match(endpoint,/session\.User\.Role != "super_admin"/);
  assert.match(endpoint,/AdminPermissions\.Has\(session\.User\.Role, "finance:read"\)/);
  assert.match(endpoint,/session\.User\.Organizations\.Any\(org => org\.Id == organization_id\)/);
  assert.match(endpoint,/Query\["organization_id"\]\.Count != 1/);
  assert.match(endpoint,/admin\.CurrentAsync\(cookie, token\)/);
  assert.match(endpoint,/SELECT hub\.read_admin_fan_subscription_overview\(@digest,@owner\)::text/);
  assert.match(endpoint,/private, no-store/);
  assert.match(endpoint,/\.ExcludeFromDescription\(\)/);
  assert.match(edge,/\['get','\/api\/admin\/hub\/fan-subscriptions\/overview'\]/);
  assert.doesNotMatch(edge,/\['post','\/api\/admin\/hub\/fan-subscriptions\/overview'\]/);
});

test('restricted SQL function privileges are explicit and raw payment tables are denied',()=>{
  assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.read_admin_fan_subscription_overview\(text,text\)/);
  assert.match(grants,/REVOKE ALL ON FUNCTION hub\.read_admin_fan_subscription_overview\(text,text\)/);
  assert.match(grants,/\\ir admin-hub-fan-overview-boundary\.sql/);
  assert.match(grants,/'hub\.read_admin_fan_subscription_overview\(text,text\)'::regprocedure/);
  assert.match(permissions,/'hub\.read_admin_fan_subscription_overview\(text,text\)'::regprocedure/);
  assert.match(boundary,/has_function_privilege\(mobile_role,routine\.oid,'EXECUTE'\)/);
  assert.match(boundary,/has_any_column_privilege\(auth_role,relation_name,'SELECT,INSERT,UPDATE,REFERENCES'\)/);
  assert.match(boundary,/has_any_column_privilege\(mobile_role,relation_name,'SELECT,INSERT,UPDATE,REFERENCES'\)/);
  assert.match(boundary,/'finance\.payment_requests','finance\.payment_notifications'/);
});

test('admin workspace is read-only, role-gated and rejects unverified summary payloads',()=>{
  assert.match(app,/user\.role==='super_admin'/);
  assert.match(app,/hasAdminPermission\(\$auth\.session\.user\.permissions,'finance:read'\)/);
  assert.match(app,/validFanHubOverview\(data,selected\)/);
  assert.match(app,/credentials:'same-origin',cache:'no-store',signal:controller\.signal/);
  assert.match(app,/auth\.invalidateSession\(\)/);
  assert.match(app,/auth\.bootstrap\(\)/);
  assert.match(app,/overview\.counts\.verified_active/);
  assert.match(app,/overview\.counts\.recorded_active/);
  assert.match(app,/sandbox/);
  assert.doesNotMatch(app,/\/checkout|method:'POST'|method:'PUT'|method:'PATCH'|{@html/);
  assert.match(dto,/Object\.keys\(value\)\.sort\(\)/);
  assert.match(dto,/d\.organization_id!==owner/);
  assert.match(dto,/tracks\.has\(String\(plan\.track\)\)/);
  assert.match(dto,/counts\.verified_active as number/);
  assert.match(catalog,/id === 'hub_subscriptions'\) return user\.role==='super_admin'/);
  assert.match(catalog,/status: 'live', href: '\/admin\/hub\/subscriptions'/);
  assert.match(frame,/liveIds\.has\('hub_subscriptions'\)/);
  assert.match(frame,/href="\/admin\/hub\/subscriptions"/);
  assert.match(app,/active="hub-subscriptions"/);
});
