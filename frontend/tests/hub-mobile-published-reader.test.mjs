import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112210000_HubMobilePublishedReader.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/MobileHubPublishedEndpoints.cs');
const store=read('backend/src/LefteriaFc.Api/Hub/MobileHubPublishedStore.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const checks=read('ops/postgres/identity-app-permissions-check.sql');
test('authenticated read is current identity and audience-only, never role from query',()=>{
 assert.match(api,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(api,/MobilePrincipal\.Read\(context\.User\)/);
 assert.match(api,/MapGet\("\/api\/mobile\/hub\/education"/);
 assert.match(store,/MobileRole\.Parent or MobileRole\.Player/);
 assert.match(store,/actor\.Role==MobileRole\.Parent\?"parent":"player"/);
 assert.match(sql,/_role NOT IN \('parent','player'\)/);
 assert.match(sql,/i\.id=_identity AND i\.normalized_phone=_phone/);
 assert.match(sql,/i\.role=_role AND i\.is_active/);
 assert.match(sql,/FOR SHARE/);
});
test('only actually published approved and independently reviewed articles are readable',()=>{
 assert.match(sql,/c\.status='published'/);
 assert.match(sql,/c\.approved_by IS NOT NULL/);
 assert.match(sql,/r\.text_review='passed' AND r\.visual_review='passed'/);
 assert.match(sql,/c\.kind='article'/);
 assert.match(sql,/c\.organization_id=_owner/);
 assert.match(sql,/LIMIT 20 OFFSET _offset/);
 assert.match(sql,/c\.age_min IS NULL AND c\.age_max IS NULL/);
 assert.match(sql,/m\.moderation<>'reviewed'/);
 assert.doesNotMatch(sql,/hub\.fan_subscriptions|payment|UPDATE hub\.content|SET status='published'/);
});
test('mobile role has read function but no HUB table or publishing permissions',()=>{
 const sig='hub.read_mobile_published_education(uuid,text,text,timestamptz,text,integer)';
 assert.ok(!grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"app_role"'));
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(checks.includes("'"+sig+"'"));
 assert.match(checks,/has_any_column_privilege\(current_user,'hub\.content'/);
 assert.match(checks,/has_function_privilege\(current_user,\s*'hub\.publish_admin_hub_content/);
 assert.match(program,/AddScoped<MobileHubPublishedStore>/);
 assert.match(program,/MapMobileHubPublished\(\)/);
});

test('published education rejects ambiguous organization scopes and malformed server payloads',()=>{
 const endpoint=read('backend/src/LefteriaFc.Api/Hub/MobileHubPublishedEndpoints.cs');
 assert.match(endpoint,/ParseOrganization\(organization_id,context\)/);
 assert.match(endpoint,/Request\.Query\["organization_id"\]\.Count!=1/);
 assert.match(endpoint,/raw is not \("lefteria_fc" or "lefteria_fc_academy"\)/);
 const store=read('backend/src/LefteriaFc.Api/Hub/MobileHubPublishedStore.cs');
 assert.match(store,/string\.IsNullOrWhiteSpace\(item\.Title\)/);
 assert.match(store,/string\.IsNullOrWhiteSpace\(item\.Body\)/);
 assert.match(store,/item\.PublishedAt==default/);
 assert.match(store,/item\.PublishedAt>DateTimeOffset\.UtcNow/);
 assert.match(store,/item\.AgeMin is not null \|\| item\.AgeMax is not null/);
 assert.match(store,/CheckLearningScope\(actor,organization\)/);
});

test('legacy article route never allows navigating the global publication catalogue',()=>{
 assert.match(store,/var page=await NextAsync\(actor,organization,token\)/);
 assert.match(store,/offset==0 && page\.Next is \{ \} article/);
 assert.match(store,/Array\.Empty<MobileHubArticle>\(\)/);
 assert.doesNotMatch(store,/SELECT hub\.read_mobile_published_education\(/);
 assert.match(checks,/IF has_function_privilege\(current_user,\s*'hub\.read_mobile_published_education/);
});
