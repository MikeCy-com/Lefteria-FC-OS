import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validPlayerQuizPage,validPlayerQuizAnswers,validPlayerQuizScore,
  validPlayerQuizStart} from '../src/lib/mobile/player-weekly-quiz.ts';

const read=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const schema=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113160000_HubPlayerWeeklyQuizFoundation.cs');
const grade=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113161000_HubPlayerWeeklyQuizScoring.cs');
const store=read('backend/src/LefteriaFc.Api/Hub/MobilePlayerWeeklyQuizStore.cs');
const endpoints=read('backend/src/LefteriaFc.Api/Hub/MobilePlayerWeeklyQuizEndpoints.cs');
const program=read('backend/src/LefteriaFc.Api/Program.cs');
const grants=read('ops/postgres/identity-app-grants.sql');
const checks=read('ops/postgres/identity-app-permissions-check.sql');
const pageComponent=read('frontend/src/lib/mobile/components/MobilePlayerWeeklyQuiz.svelte');
const controller=read('frontend/src/lib/mobile/controller.ts');
const appRoute=read('frontend/src/routes/app/[...path]/+page.svelte');
const id='11111111-1111-4111-8111-111111111111';
const questions=[1,2,3].map(i=>({id:`11111111-1111-4111-8111-11111111111${i}`,
  sequence:i,prompt:'Where should a defender position themselves?',
  choices:['Between goal and attacker','At opposite corner','Only beside referee','Off the field']}));
const quiz={id,title:'Football game understanding',topic:'defending',
  position:'all',week_number:1,questions};
const page={organization_id:'lefteria_fc_academy',started:true,quiz};

test('late registrant explicitly starts Week 1 without a global join-date offset',()=>{
 assert.match(schema,/CREATE TABLE hub\.player_quiz_tracks/);
 assert.match(schema,/PRIMARY KEY\(identity_id,organization_id\)/);
 assert.match(schema,/INSERT INTO hub\.player_quiz_tracks\(identity_id,organization_id\)/);
 assert.match(schema,/ON CONFLICT DO NOTHING/);
 assert.match(schema,/q\.week_number<=1\+floor\(extract\(epoch from/);
 assert.match(schema,/clock_timestamp\(\)-v_started/);
 assert.match(schema,/ORDER BY q\.week_number ASC/);
 assert.match(schema,/NOT EXISTS\(/);
 assert.doesNotMatch(schema,/FROM hub\.fan_subscriptions|JOIN hub\.fan_subscriptions/);
});
test('weekly football question bank requires human publication and restricts answer keys',()=>{
 for(const word of ['positioning','defending','attacking','transition',
   'winger','fullback','goalkeeper','midfielder','striker'])
   assert.ok(schema.includes("'"+word+"'"));
 assert.match(schema,/reviewed_by IS NOT NULL AND q\.reviewed_at IS NOT NULL/);
 assert.match(schema,/q\.status='published'/);
 assert.match(schema,/q\.position='all'/);
 assert.match(schema,/jsonb_build_object\(\s*'id',qq\.id,'sequence',qq\.sequence,'prompt',qq\.prompt,/);
 assert.doesNotMatch(schema,/jsonb_build_object\([\s\S]{0,120}'correct_index'/);
 assert.match(schema,/_role IS DISTINCT FROM 'player'/);
 assert.match(schema,/FOR SHARE/);
});
test('submission is complete, sequential, private, one attempt per quiz',()=>{
 assert.match(grade,/v_page:=hub\.read_mobile_player_weekly_quiz/);
 assert.match(grade,/WHERE identity_id=_identity AND organization_id=_owner FOR UPDATE/);
 assert.match(grade,/v_current<>_quiz/);
 assert.match(grade,/v_total<>jsonb_array_length\(_answers\)/);
 assert.match(grade,/v_question=ANY\(v_seen\)/);
 assert.match(grade,/quiz_id=_quiz AND id=v_question/);
 assert.match(grade,/INSERT INTO hub\.player_quiz_attempts/);
 assert.match(schema,/PRIMARY KEY\(identity_id,organization_id,quiz_id\)/);
 assert.match(grade,/'score',v_score,'total',v_total/);
 assert.doesNotMatch(grade,/correct_index',|right_answer'/);
});
test('runtime never reads unpublished question keys or other player scores',()=>{
 assert.match(grants,/REVOKE ALL ON TABLE hub\.player_quizzes,hub\.player_quiz_questions/);
 assert.match(grants,/REVOKE ALL ON FUNCTION hub\.assert_current_quiz_player/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.start_mobile_player_quiz_track/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.read_mobile_player_weekly_quiz/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.submit_mobile_player_weekly_quiz/);
 assert.match(checks,/Fan bypassed player weekly quiz boundary/);
 assert.match(endpoints,/RequireAuthorization\(IdentityServices\.Policy\)/);
 assert.match(endpoints,/Headers\.CacheControl="private, no-store"/);
 assert.match(store,/actor\.Role!=MobileRole\.Player/);
 assert.match(program,/AddScoped<MobilePlayerWeeklyQuizStore>/);
 assert.match(program,/MapMobilePlayerWeeklyQuiz\(\)/);
});
test('mobile rejects spoofed answer keys and submits all 3 questions only',()=>{
 assert.equal(validPlayerQuizPage(page,'lefteria_fc_academy'),true);
 assert.equal(validPlayerQuizPage({...page,quiz:{...quiz,position:'winger'}},'lefteria_fc_academy'),false);
 assert.equal(validPlayerQuizPage({...page,quiz:{...quiz,questions:[
   {...questions[0],correct_index:0},...questions.slice(1)]}},'lefteria_fc_academy'),false);
 const answers=questions.map(q=>({question_id:q.id,selected_index:0}));
 assert.equal(validPlayerQuizAnswers(quiz,answers),true);
 assert.equal(validPlayerQuizAnswers(quiz,answers.slice(1)),false);
 assert.equal(validPlayerQuizAnswers(quiz,[answers[0],answers[0],answers[2]]),false);
 assert.equal(validPlayerQuizStart({organization_id:page.organization_id,
   started_at:'2026-11-01T00:00:00Z'},page.organization_id),true);
 assert.equal(validPlayerQuizScore({organization_id:page.organization_id,
   quiz_id:quiz.id,score:2,total:3,submitted:true},page.organization_id,quiz),true);
 assert.match(controller,/async startPlayerWeeklyQuiz/);
 assert.match(controller,/async getPlayerWeeklyQuiz/);
 assert.match(controller,/async submitPlayerWeeklyQuiz/);
 assert.match(pageComponent,/controller\.submitPlayerWeeklyQuiz/);
 assert.match(appRoute,/MobilePlayerWeeklyQuiz \{controller\}/);
 assert.doesNotMatch(pageComponent,/localStorage|{@html|innerHTML/);
});
