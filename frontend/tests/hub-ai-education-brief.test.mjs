import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112120000_HubAiAudienceBriefQueue.cs');
const api=read('backend/src/LefteriaFc.Api/Hub/AdminHubEndpoints.cs');
const grants=read('ops/postgres/web-admin-auth-grants.sql');
const verify=read('ops/postgres/web-admin-auth-permissions-check.sql');
const boundary=read('ops/postgres/admin-hub-editorial-boundary.sql');

test('AI education briefs force one separate audience, bounded age and topics',()=>{
 assert.match(sql,/CREATE TABLE hub\.ai_education_briefs/);
 assert.match(sql,/audience IN \('parent','player'\)/);
 assert.match(sql,/age_min BETWEEN 6 AND 18 AND age_max BETWEEN age_min AND 18/);
 assert.match(sql,/audience='parent' AND topic IN \('support','communication','development','wellbeing'\)/);
 assert.match(sql,/audience='player' AND topic IN \('confidence','teamwork','resilience','learning','wellbeing'\)/);
 assert.match(sql,/language IN \('el','en'\)/);
 assert.match(sql,/status='pending'/);
 assert.doesNotMatch(sql,/player_id|parent_id|phone_number|prompt_text|provider_key|generated_output|status='published'/);
});
test('AI briefing never bypasses authorization or enqueues arbitrary free-form personal data',()=>{
 assert.match(api,/MapPost\("\/api\/admin\/hub\/ai\/education-briefs"/);
 assert.match(api,/HubAiEducationBriefCommand\(string Audience, short AgeMin, short AgeMax, string Topic, string Language\)/);
 assert.match(api,/RequireAuthorization\(WebAdminServices\.Policy\)/);
 assert.match(api,/AdminPermissions\.Has\(session\.User\.Role, "news:write"\)/);
 assert.match(api,/SELECT hub\.request_admin_ai_education\(@digest,@owner,@id,@audience,@age_min,@age_max,@topic,@language\)::text/);
 assert.match(api,/private, no-store/);
 assert.match(sql,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
 assert.match(sql,/v_role NOT IN \('manager','super_admin','editor'\)/);
 assert.match(sql,/INSERT INTO audit\.security_events/);
 assert.match(sql,/SECURITY DEFINER/);
 assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
});
test('AI queue table stays inaccessible from mobile and only exact admin routine can request',()=>{
 const sig='hub.request_admin_ai_education(text,text,uuid,text,smallint,smallint,text,text)';
 assert.match(sql,/ALTER TABLE hub\.ai_education_briefs ENABLE ROW LEVEL SECURITY/);
 assert.match(sql,/REVOKE ALL ON TABLE hub\.ai_education_briefs FROM PUBLIC/);
 assert.ok(grants.includes('REVOKE ALL ON FUNCTION '+sig+' FROM PUBLIC, :"app_role"'));
 assert.ok(grants.includes('GRANT EXECUTE ON FUNCTION '+sig+' TO :"admin_auth_role"'));
 assert.ok(verify.includes("'"+sig+"'::regprocedure"));
 assert.ok(boundary.includes("'"+sig+"'::regprocedure"));
 assert.match(boundary,/has_function_privilege\(mobile_role,ai_brief_writer\.oid,'EXECUTE'\)/);
});
