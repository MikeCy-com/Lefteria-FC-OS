import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';

const quiz=readFileSync(new URL('../src/lib/mobile/components/MobilePlayerWeeklyQuiz.svelte',import.meta.url),'utf8');
const lessons=readFileSync(new URL('../src/lib/mobile/components/MobilePersonalLearningJourney.svelte',import.meta.url),'utf8');

test('player quiz and published lesson views compile in Svelte 5',()=>{
  for(const [name,source] of [['MobilePlayerWeeklyQuiz',quiz],['MobilePersonalLearningJourney',lessons]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
});
test('quiz completion requires every valid answer and labels progress without exposing keys',()=>{
  assert.match(quiz,/const questionCount=\$derived\(page\?\.quiz\?\.questions\.length\?\?0\)/);
  assert.match(quiz,/chosen\[question\.id\]>=0&&chosen\[question\.id\]<question\.choices\.length/);
  assert.match(quiz,/questionCount>0&&answeredCount===questionCount/);
  assert.match(quiz,/data-testid="player-quiz-progress"/);
  assert.match(quiz,/<progress class="quiz-progress-bar"/);
  assert.match(quiz,/disabled=\{!complete\|\|busy\}/);
  assert.match(quiz,/aria-describedby="quiz-submit-help"/);
  assert.match(quiz,/role="status"/);
  assert.match(quiz,/const answers:PlayerQuizAnswer\[\]=quiz\.questions\.map/);
  assert.doesNotMatch(quiz,/correct_index|correctIndex|innerHTML|localStorage|\{@html/);
});
test('quiz and personal lesson organization choices expose semantic selected state',()=>{
  for(const source of [quiz,lessons]){
    assert.match(source,/aria-pressed=\{organization==='lefteria_fc_academy'\}/);
    assert.match(source,/aria-pressed=\{organization==='lefteria_fc'\}/);
    assert.match(source,/min-height:44px/);
  }
});
test('personal lesson shows only next approved content and server-backed schedule',()=>{
  assert.match(lessons,/data-testid="hub-next-approved-lesson"/);
  assert.match(lessons,/page\.next\.title/);
  assert.match(lessons,/page\.next\.body/);
  assert.match(lessons,/controller\.completeNextHubLesson\(submitted\)/);
  assert.match(lessons,/aria-describedby="hub-completion-guidance"/);
  assert.match(lessons,/<time datetime=\{schedule\.next_due_at\}>/);
  assert.match(lessons,/controller\.getPersonalLessonDeliveries\(organization\)/);
  assert.match(lessons,/result\.audience===viewerRole/);
  assert.doesNotMatch(lessons,/localStorage|sessionStorage|innerHTML|\{@html/);
});
