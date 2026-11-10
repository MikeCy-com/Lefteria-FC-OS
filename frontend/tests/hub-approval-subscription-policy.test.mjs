import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { transitionHubContent, canAccessSubscribedHub } from '../src/lib/hub/hub-policy.ts';

const draft = {
  id:'demo-article', organizationId:'lefteria_fc_academy', status:'ai_draft', version:0,
  title:'Δοκιμαστικό άρθρο', audience:['parent','player'], textReview:'pending', visualReview:'pending',
  provenance:'ai', approvedBy:null, publishedAt:null
};
const agent = { id:'content-ai', kind:'ai' };
const editor = { id:'editor-1', kind:'editor' };
const admin = { id:'admin-1', kind:'admin' };

test('AI content and visuals cannot publish or self-approve', () => {
  assert.throws(() => transitionHubContent(draft,'approve',agent,0,'2026-11-10T10:00:00Z'), /ai_cannot_approve_or_publish/);
  assert.throws(() => transitionHubContent(draft,'publish',agent,0,'2026-11-10T10:00:00Z'), /ai_cannot_approve_or_publish/);
  const submitted=transitionHubContent(draft,'submit_review',agent,0,'2026-11-10T10:00:00Z');
  assert.equal(submitted.status,'review_required');
  assert.throws(() => transitionHubContent(submitted,'approve',editor,1,'2026-11-10T10:00:00Z'), /admin_approval_required/);
  assert.throws(() => transitionHubContent(submitted,'approve',admin,1,'2026-11-10T10:00:00Z'), /text_and_visual_approval_required/);
  assert.throws(() => transitionHubContent(submitted,'publish',admin,1,'2026-11-10T10:00:00Z'), /invalid_editorial_transition/);
  const reviewed={...submitted,textReview:'passed',visualReview:'passed'};
  const approved=transitionHubContent(reviewed,'approve',admin,1,'2026-11-10T10:00:00Z');
  assert.equal(approved.status,'approved');
  assert.equal(approved.approvedBy,'admin-1');
  const published=transitionHubContent(approved,'publish',admin,2,'2026-11-10T11:00:00Z');
  assert.equal(published.status,'published');
  assert.equal(published.publishedAt,'2026-11-10T11:00:00Z');
  assert.throws(() => transitionHubContent(reviewed,'approve',admin,0,'2026-11-10T10:00:00Z'), /editorial_version_conflict/);
});

test('revision clears approval and publish evidence', () => {
  const queued=transitionHubContent(draft,'submit_review',agent,0,'2026-11-10T10:00:00Z');
  const approved=transitionHubContent({...queued,textReview:'passed',visualReview:'passed'},'approve',admin,1,'2026-11-10T10:00:00Z');
  const revised=transitionHubContent(approved,'revise',editor,2,'2026-11-10T12:00:00Z');
  assert.equal(revised.status,'ai_draft');
  assert.equal(revised.approvedBy,null);
  assert.equal(revised.textReview,'pending');
  assert.equal(revised.visualReview,'pending');
  assert.equal(revised.publishedAt,null);
});

test('fan content requires provider-confirmed, unexpired audience-scoped entitlement', () => {
  const active={subscriberId:'fan-1',status:'active',expiresAt:'2026-12-10T10:00:00Z',
    audiences:['parent'],confirmedByProvider:true};
  const check=(who,role,subscription,audience='parent')=>
    canAccessSubscribedHub(who,role,subscription,audience,'2026-11-10T10:00:00Z');
  assert.equal(check('fan-1','fan',active),true);
  assert.equal(check('fan-1','fan',active,'player'),false);
  assert.equal(check('fan-2','fan',active),false);
  assert.equal(check('fan-1','parent',active),false);
  assert.equal(check('fan-1','fan',{...active,confirmedByProvider:false}),false);
  for(const status of ['pending','past_due','cancelled','expired'])
    assert.equal(check('fan-1','fan',{...active,status}),false);
  assert.equal(check('fan-1','fan',{...active,expiresAt:'2026-11-10T10:00:00Z'}),false);
  assert.equal(check('fan-1','fan',null),false);
});

test('fan app stays locked until a protected subscription API is implemented', () => {
  const ui=readFileSync(new URL('../src/lib/mobile/components/MobileAcademyEducation.svelte',import.meta.url),'utf8');
  assert.match(ui,/viewerRole === 'fan'/);
  assert.match(ui,/data-testid="hub-subscription-required"/);
  assert.match(ui,/Το περιεχόμενο παραμένει κλειδωμένο/);
  assert.match(ui,/{:else}\s*<section class="education-articles"/);
  assert.doesNotMatch(ui,/fetch\(|checkout\(|paymentUrl|automaticCharge/);
});
