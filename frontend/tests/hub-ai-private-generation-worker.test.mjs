import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261112140000_HubAiEducationWorker.cs');
const grants=read('ops/postgres/hub-ai-worker-grants.sql');
const store=read('backend/src/LefteriaFc.Api/Hub/HubAiEducationStore.cs');
const provider=read('backend/src/LefteriaFc.Api/Hub/HubEducationProvider.cs');
const settings=read('backend/src/LefteriaFc.Api/Hub/HubAiEducationSettings.cs');
const worker=read('backend/src/LefteriaFc.Api/Hub/HubAiEducationWorker.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');

test('leased AI worker enforces retry and bounded per-club daily quota',()=>{
 assert.match(sql,/CREATE FUNCTION hub\.claim_ai_education_brief\(_lease uuid\)/);
 assert.match(sql,/pg_advisory_xact_lock\(520261112\)/);
 assert.match(sql,/FOR UPDATE SKIP LOCKED/);
 assert.match(sql,/b\.attempts<3/);
 assert.match(sql,/e\.organization_id=b\.organization_id/);
 assert.match(sql,/e\.event='claimed'/);
 assert.match(sql,/\) < 10/);
 assert.match(sql,/\) < 20/);
 assert.match(sql,/interval '90 seconds'/);
 assert.match(sql,/lease_id IS NOT NULL AND leased_until IS NOT NULL/);
 assert.match(sql,/ALTER TABLE hub\.ai_education_job_events ENABLE ROW LEVEL SECURITY/);
 assert.match(sql,/SECURITY DEFINER/);
 assert.match(sql,/SET search_path=pg_catalog,pg_temp/);
});
test('the worker stores only provenance-backed unapproved private drafts',()=>{
 assert.match(sql,/CREATE FUNCTION hub\.complete_ai_education_brief/);
 assert.match(sql,/v_job\.lease_id IS DISTINCT FROM _lease/);
 assert.match(sql,/v_job\.leased_until<=clock_timestamp\(\)/);
 assert.match(sql,/status='ai_draft'|'ai_draft'/);
 assert.match(sql,/'ai',1/);
 assert.match(sql,/text_review,\s*visual_review,source_provenance/);
 assert.match(sql,/'pending','pending'/);
 assert.match(sql,/prompt_version IS DISTINCT FROM 'education-v1'/);
 assert.match(sql,/hub\.review_events/);
 assert.doesNotMatch(sql,/SET status='published'|SET status='approved'|DELETE FROM hub\.content|INSERT INTO club\.app_private_messages/);
});
test('worker is opt-in with pinned provider, budget and content moderation',()=>{
 assert.match(settings,/LEFTERIA_AI_ENABLED/);
 assert.match(settings,/LEFTERIA_AI_API_KEY/);
 assert.match(settings,/LEFTERIA_AI_WORKER_CONNECTION/);
 assert.match(settings,/LEFTERIA_AI_MODEL/);
 assert.match(worker,/if\(!settings\.Enabled\)return/);
 assert.match(worker,/store\.ClaimAsync\(stoppingToken\)/);
 assert.match(worker,/store\.CompleteAsync/);
 assert.match(worker,/store\.FailAsync/);
 assert.match(provider,/https:\/\/api\.openai\.com\/v1\/responses/);
 assert.match(provider,/https:\/\/api\.openai\.com\/v1\/moderations/);
 assert.match(provider,/store=false/);
 assert.match(provider,/max_output_tokens=1200/);
 assert.match(provider,/tool_choice="none"/);
 assert.match(provider,/format=new/);
 assert.match(provider,/moderation_blocked/);
 assert.match(provider,/No personal names/);
 assert.match(provider,/Never impersonate a coach/);
 assert.match(program,/AddHostedService<HubAiEducationWorker>/);
 assert.match(program,/AllowAutoRedirect=false,UseCookies=false/);
});
test('no raw table or publishing rights are conferred to the AI worker',()=>{
 assert.match(grants,/ai_worker_role/);
 assert.match(grants,/GRANT USAGE ON SCHEMA hub/);
 assert.match(grants,/REVOKE ALL ON ALL TABLES IN SCHEMA hub/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.claim_ai_education_brief/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.complete_ai_education_brief/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.fail_ai_education_brief/);
 assert.match(grants,/AI worker effective function permissions unsafe/);
 assert.doesNotMatch(grants,/GRANT EXECUTE ON FUNCTION hub\.review_admin_text|GRANT EXECUTE ON FUNCTION hub\.create_admin_draft|GRANT SELECT ON/);
 assert.doesNotMatch(store,/club\.players|identity\.mobile_identities|hub\.review_admin_text|status='published'/);
});

test('AI provider response bodies are bounded while streaming and require valid UTF-8',()=>{
 assert.match(provider,/ReadAsStreamAsync\(token\)/);
 assert.match(provider,/buffer\.Length\+count>65536/);
 assert.match(provider,/UTF8Encoding\(false,true\)/);
 assert.match(provider,/DecoderFallbackException/);
 assert.doesNotMatch(provider,/ReadAsStringAsync\(token\)/);
});
