import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import ts from 'typescript';
import {compile} from 'svelte/compiler';

const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
const controller=read('frontend/src/lib/mobile/controller.ts');
const component=read('frontend/src/lib/mobile/components/MobileCoachFeedbackComposer.svelte');
const route=read('frontend/src/routes/app/[...path]/+page.svelte');
const validators=read('frontend/src/lib/mobile/coach-feedback-composer.ts');

test('coach Squad mounts guarded three-step feedback composer only for coach',()=>{
 assert.match(route,/import MobileCoachFeedbackComposer from/);
 assert.match(route,/activeTab === 'squad' && role === 'coach'/);
 assert.match(route,/<MobileCoachFeedbackComposer \{controller\}\/>/);
 assert.doesNotMatch(route,/role === 'parent'[^\n]*<MobileCoachFeedbackComposer/);
 assert.doesNotMatch(route,/role === 'fan'[^\n]*<MobileCoachFeedbackComposer/);
 assert.match(component,/getCoachFeedbackTargets\(\)/);
 assert.match(component,/createCoachFeedbackDraft\(selected,observation,focusPoint,language\)/);
 assert.match(component,/const submittedText=finalText;/);
 assert.match(component,/approveCoachFeedbackDraft\(draft,submittedText\)/);
 assert.match(component,/signedText=submittedText;/);
 assert.match(component,/sendCoachFeedback\(approved\)/);
 assert.match(component,/acknowledgeApproval/);
 assert.match(component,/acknowledgeDelivery/);
 assert.match(component,/stage==='edit'/);
 assert.match(component,/stage==='draft'/);
 assert.match(component,/stage==='approved'/);
 assert.match(component,/stage==='sent'/);
 assert.doesNotMatch(component,/fetch\(|localStorage|{@html}|innerHTML|AI agent.*sendAsync/);
 const result=compile(component,{filename:'MobileCoachFeedbackComposer.svelte',generate:'server'});
 assert.ok(result.js.code.length>0);
});
test('mobile client maintains signed-in coach role and validates each server response',()=>{
 assert.match(controller,/state\.user\?\.role!=='coach'/);
 assert.match(controller,/state\.user\.current_role_available/);
 assert.match(controller,/chatRequest\('\/api\/mobile\/coach\/feedback\/targets'\)/);
 assert.match(controller,/validCoachFeedbackTargets\(result\)/);
 assert.match(controller,/organizationId:target\.organization_id,playerId:target\.id/);
 assert.match(controller,/validCoachDraft\(result,target,language\)/);
 assert.match(controller,/validCoachApproval\(result,draft\)/);
 assert.match(controller,/validCoachSend\(result,approved\)/);
 assert.match(controller,/approved\.authorized_by_coach/);
 assert.match(controller,/version:1,finalText/);
 assert.match(controller,/version:2/);
});
test('strict coach DTO validators reject forged authorizations, cross-owner or duplicate targets',()=>{
 const out=ts.transpileModule(validators,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022}});
 const exports={};
 new Function('exports',out.outputText)(exports);
 const {validCoachFeedbackTargets,validCoachDraft,validCoachApproval,validCoachSend,validCoachText}=exports;
 const target={id:'u10_player',organization_id:'lefteria_fc_academy',name:'Player'};
 const draft={id:'11111111-2222-4333-8444-555555555555',organization_id:target.organization_id,
   player_id:target.id,status:'draft',version:1,language:'el',requires_coach_approval:true};
 const signed={id:draft.id,organization_id:target.organization_id,player_id:target.id,
   status:'approved',version:2,authorized_by_coach:true,delivered:false};
 const sent={id:draft.id,organization_id:target.organization_id,status:'sent',
   version:3,inbox_available:true,read:false};
 assert.equal(validCoachFeedbackTargets({items:[target]}),true);
 assert.equal(validCoachFeedbackTargets({items:[target,target]}),false);
 assert.equal(validCoachFeedbackTargets({items:[{...target,name:'Fake\nCoach'}]}),false);
 assert.equal(validCoachDraft(draft,target,'el'),true);
 assert.equal(validCoachDraft({...draft,requires_coach_approval:false},target,'el'),false);
 assert.equal(validCoachDraft({...draft,player_id:'other'},target,'el'),false);
 assert.equal(validCoachApproval(signed,draft),true);
 assert.equal(validCoachApproval({...signed,delivered:true},draft),false);
 assert.equal(validCoachSend(sent,signed),true);
 assert.equal(validCoachSend({...sent,read:true},signed),false);
 assert.equal(validCoachText('Recorded improvement',5,500),true);
 assert.equal(validCoachText(' Unsafe\ntext',5,500),false);
});
