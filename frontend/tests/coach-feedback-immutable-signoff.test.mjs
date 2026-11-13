import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validCoachApproval,validCoachSend,validCoachText} from '../src/lib/mobile/coach-feedback-composer.ts';

const component=readFileSync(new URL('../src/lib/mobile/components/MobileCoachFeedbackComposer.svelte',import.meta.url),'utf8');

test('coach signoff freezes the exactly submitted final text in the approved and sent view',()=>{
  assert.match(component,/const submittedText=finalText;/);
  assert.match(component,/approveCoachFeedbackDraft\(draft,submittedText\)/);
  assert.match(component,/signedText=submittedText;/);
  assert.match(component,/<p class="message">\{signedText\}<\/p>/);
  assert.match(component,/id="coach-feedback-final"[^>]*disabled=\{busy\}/);
  assert.match(component,/bind:checked=\{acknowledgeApproval\} disabled=\{busy\}/);
  assert.match(component,/bind:checked=\{acknowledgeDelivery\} disabled=\{busy\}/);
  assert.match(component,/Par[a-zA-Z]*|Παραλήπτης:/);
});
test('a follow-up message starts blank and reloads server-verified coach targets',()=>{
  assert.match(component,/stage='edit';draft=null;approved=null;signedText='';finalText='';/);
  assert.match(component,/observation='';focusPoint='';acknowledgeApproval=false;acknowledgeDelivery=false;/);
  assert.match(component,/selectedKey='';error=null;void loadTargets\(\);/);
  assert.match(component,/return\(\)=>\{alive=false;targets=\[\];draft=null;approved=null;signedText='';\}/);
});
test('server proof remains required for signoff and delivery and rejects unrelated target',()=>{
  const draft={id:'11111111-1111-4111-8111-111111111111',organization_id:'lefteria_fc_academy',
    player_id:'player1',status:'draft',version:1,language:'el',requires_coach_approval:true};
  const approved={id:draft.id,organization_id:draft.organization_id,player_id:draft.player_id,
    status:'approved',version:2,authorized_by_coach:true,delivered:false};
  assert.equal(validCoachApproval(approved,draft),true);
  assert.equal(validCoachApproval({...approved,player_id:'other'},draft),false);
  assert.equal(validCoachSend({id:draft.id,organization_id:draft.organization_id,status:'sent',
    version:3,inbox_available:true,read:false},approved),true);
  assert.equal(validCoachText('A real coaching observation',5,500),true);
});
