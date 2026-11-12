import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import ts from 'typescript';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const component=read('frontend/src/lib/mobile/components/MobilePlayerCoachFeedback.svelte');
const validation=read('frontend/src/lib/mobile/coach-feedback.ts');

test('player Hub mounts own private inbox; parent and fan cannot mount it',()=>{
 assert.match(route,/import MobilePlayerCoachFeedback from/);
 assert.match(route,/\{#if role === 'player'\}\s*<MobilePlayerCoachFeedback \{controller\}/);
 assert.match(route,/MobileAcademyEducation viewerRole=\{role\}/);
 assert.doesNotMatch(route,/\{#if role === 'parent'\}\s*<MobilePlayerCoachFeedback/);
});
test('inbox fetch uses authenticated mobile client and rejects stale role or invalid results',()=>{
 assert.match(controller,/state\.user\?\.role!=='player'/);
 assert.match(controller,/!state\.user\.current_role_available/);
 assert.match(controller,/chatRequest\('\/api\/mobile\/player\/coach-feedback\?offset='/);
 assert.match(controller,/validPlayerCoachFeedbackPage\(result\)/);
 assert.match(controller,/Number\.isSafeInteger\(offset\)/);
 assert.match(controller,/offset>10000/);
});
test('inflight inbox results are invalidated across same-role player identity switches',()=>{
 assert.match(controller,/const actorId=state\.user\.id,actorToken=token,actorRevision=revision/);
 assert.match(controller,/state\.user\.id!==actorId/);
 assert.match(controller,/token!==actorToken/);
 assert.match(controller,/revision!==actorRevision/);
 assert.match(controller,/if\(!actorId\|\|!actorToken\)/);
});
test('the actual visible app does not display unverified or optimistic messages',()=>{
 assert.match(component,/onMount\(\(\)=>/);
 assert.match(component,/getPlayerCoachFeedback\(0\)/);
 assert.match(component,/getPlayerCoachFeedback\(offset\)/);
 assert.match(component,/if\(!alive\|\|current!==request\)return/);
 assert.match(component,/page\.items\.filter\(item=>!previous\.has\(item\.id\)\)/);
 assert.match(component,/new Date\(item\.sent_at\)/);
 assert.match(component,/\{item\.coach_name\}/);
 assert.match(component,/\{item\.message\}/);
 assert.match(component,/role="alert"/);
 assert.doesNotMatch(component,/innerHTML|{@html}|mark.*[Rr]ead|send.*notification/i);
});
test('response validator caps page and checks exact identity independent content',()=>{
 assert.match(validation,/value\.items\.length>20/);
 assert.match(validation,/ids\.has\(item\.id\)/);
 assert.match(validation,/item\.organization_id/);
 assert.match(validation,/item\.coach_name\.length>200/);
 assert.match(validation,/item\.message\.length>1600/);
 assert.match(validation,/Date\.parse\(item\.sent_at\)/);
 const result=ts.transpileModule(validation,{
   compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}});
 const exports={};
 new Function('exports',result.outputText)(exports);
 const valid=exports.validPlayerCoachFeedbackPage;
 const message={id:'11111111-2222-4333-8444-555555555555',
   organization_id:'lefteria_fc_academy',coach_name:'Coach',
   message:'Good effort this month. Keep looking up before your first touch.',
   sent_at:'2026-11-12T12:00:00Z'};
 assert.equal(valid({items:[message]}),true);
 assert.equal(valid({items:[message,message]}),false);
 assert.equal(valid({items:[{...message,message:'bad'}]}),false);
 assert.equal(valid({items:[{...message,organization_id:'other'}]}),false);
 assert.equal(valid({items:[{...message,coach_name:'Coach\nFake'}]}),false);
 assert.equal(valid({items:[{...message,sent_at:'invalid'}]}),false);
 assert.equal(valid({items:Array(21).fill(message)}),false);
});
