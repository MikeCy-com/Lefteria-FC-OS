import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {validPlayerQuizPage,validPlayerQuizAnswers,validPlayerQuizScore,
  validPlayerQuizStart} from '../src/lib/mobile/player-weekly-quiz.ts';

import {validAdminQuizDraft,validAdminQuizList,validAdminQuizDetail,
  validAdminQuizTransition} from '../src/lib/admin/player-quiz-editorial.ts';

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
 assert.match(schema,/q\.week_number=1\+\(/);
 assert.match(schema,/count\(DISTINCT completed\.week_number\)::integer/);
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
 assert.match(schema,/q\.position=v_position/);
 assert.match(schema,/specialized\.position=v_position/);
 assert.match(schema,/p\.organization_id=_owner/);
 assert.match(schema,/l\.collection='players' AND l\.kind='player_own'/);
 assert.match(schema,/identity\.canonical_contact_bindings/);
 assert.match(schema,/WHEN 'Defender' THEN 'defender'/);
 assert.match(schema,/WHEN 'Midfielder' THEN 'midfielder'/);
 assert.match(schema,/WHEN 'Goalkeeper' THEN 'goalkeeper'/);
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
 assert.equal(validPlayerQuizPage({...page,quiz:{...quiz,position:'defender'}},'lefteria_fc_academy'),true);
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

test('human manager separately approves reviewed quiz after another editor creates it',()=>{
 const draft=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113162000_HubPlayerQuizEditorialDraft.cs');
 const decisions=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113163000_HubPlayerQuizEditorialApproval.cs');
 const adminApi=read('backend/src/LefteriaFc.Api/Hub/AdminPlayerWeeklyQuizEndpoints.cs');
 const adminPage=read('frontend/src/routes/admin/hub/player-quizzes/+page.svelte');
 const hubPage=read('frontend/src/routes/admin/hub/+page.svelte');
 const adminGrants=read('ops/postgres/web-admin-auth-grants.sql');
 assert.match(draft,/CREATE FUNCTION hub\.authorize_admin_player_quiz/);
 assert.match(draft,/identity\.lock_admin_club_authority\(_digest,_owner\)/);
 assert.match(draft,/status,'review_required'|\'review_required\',v_actor/);
 assert.match(decisions,/v_quiz\.created_by=v_actor/);
 assert.match(decisions,/v_quiz\.status<>'review_required'/);
 assert.match(decisions,/v_quiz\.status<>'approved'/);
 assert.match(decisions,/status='published',published_at=clock_timestamp\(\)/);
 assert.match(adminApi,/MapPost\("\/api\/admin\/hub\/player-quizzes"/);
 assert.match(adminApi,/MapPost\("\/api\/admin\/hub\/player-quizzes\/\{id:guid\}\/approve"/);
 assert.match(adminApi,/MapPost\("\/api\/admin\/hub\/player-quizzes\/\{id:guid\}\/publish"/);
 assert.match(adminApi,/RequireAuthorization\(WebAdminServices\.Policy\)/);
 assert.match(adminApi,/Headers\.CacheControl="private, no-store"/);
 assert.match(adminGrants,/GRANT EXECUTE ON FUNCTION hub\.create_admin_player_quiz/);
 assert.match(adminGrants,/GRANT EXECUTE ON FUNCTION hub\.approve_admin_player_quiz/);
 assert.match(adminGrants,/GRANT EXECUTE ON FUNCTION hub\.publish_admin_player_quiz/);
 assert.match(adminGrants,/REVOKE ALL ON TABLE hub\.player_quizzes,hub\.player_quiz_questions/);
 assert.match(adminPage,/validAdminQuizDraft\(draft\)/);
 assert.match(adminPage,/validAdminQuizDetail\(v,owner,item\.id\)/);
 assert.match(adminPage,/transition\('approve'\)/);
 assert.match(adminPage,/transition\('publish'\)/);
 assert.match(hubPage,/href="\/admin\/hub\/player-quizzes"/);
 assert.doesNotMatch(pageComponent,/correct_index|correctIndex|{@html|innerHTML/);
});
test('editorial validation protects private quiz answer keys and admin actions',()=>{
 const entry={week_number:1,topic:'general',position:'all',
   title:'Football game understanding',questions:questions.map(q=>({
     prompt:q.prompt,choices:q.choices,correct_index:0,explanation:''}))};
 assert.equal(validAdminQuizDraft(entry),true);
 assert.equal(validAdminQuizDraft({...entry,questions:entry.questions.slice(1)}),false);
 assert.equal(validAdminQuizDraft({...entry,questions:[
   {...entry.questions[0],choices:['x','x','y','z']},...entry.questions.slice(1)]}),false);
 const summary={id,title:entry.title,week_number:1,topic:'general',
   position:'all',status:'review_required',question_count:3};
 assert.equal(validAdminQuizList({organization_id:'lefteria_fc_academy',
   items:[summary]},'lefteria_fc_academy'),true);
 const detail={...summary,organization_id:'lefteria_fc_academy',
   questions:entry.questions.map((q,i)=>({...q,id:questions[i].id,sequence:i+1}))};
 assert.equal(validAdminQuizDetail(detail,'lefteria_fc_academy',id),true);
 assert.equal(validAdminQuizTransition({id,organization_id:'lefteria_fc_academy',
   status:'approved'},'lefteria_fc_academy',id,'approved'),true);
});
test('twelve editorial football lesson and quiz weeks remain unpublished pending review',()=>{
 const draft=JSON.parse(read('ops/content/editorial/player-football-first-12-weeks-drafts.json'));
 assert.equal(draft.approval_required,true);
 assert.equal(draft.content.length,12);
 assert.deepEqual(draft.content.map(x=>x.week_number),Array.from({length:12},(_,i)=>i+1));
 assert.ok(draft.content.every(x=>x.status==='draft_unapproved'));
 assert.ok(draft.content.every(x=>x.quiz.questions.length===3));
 assert.ok(draft.content.some(x=>x.topic==='transition'));
 assert.ok(draft.content.some(x=>x.topic==='defending'));
 assert.ok(draft.content.some(x=>/εξτρέμ/.test(x.lesson.title+' '+x.lesson.objective)));
 assert.ok(draft.content.every(x=>x.quiz.questions.every(q=>
   q.choices.length===4&&q.correct_index>=0&&q.correct_index<4)));
});

test('withdrawal requires a human manager and never erases private student results',()=>{
 const withdrawal=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261113164000_HubPlayerQuizEditorialWithdrawal.cs');
 const endpoint=read('backend/src/LefteriaFc.Api/Hub/AdminPlayerWeeklyQuizEndpoints.cs');
 const admin=read('frontend/src/routes/admin/hub/player-quizzes/+page.svelte');
 const grants=read('ops/postgres/web-admin-auth-grants.sql');
 const boundary=read('ops/postgres/admin-hub-quiz-boundary.sql');
 assert.match(withdrawal,/hub\.authorize_admin_player_quiz\(_digest,_owner,true\)/);
 assert.match(withdrawal,/status='withdrawn',published_at=NULL/);
 assert.match(withdrawal,/INSERT INTO hub\.player_quiz_review_events/);
 assert.doesNotMatch(withdrawal,/DELETE FROM hub\.player_quiz_attempts/);
 assert.match(endpoint,/MapPost\("\/api\/admin\/hub\/player-quizzes\/\{id:guid\}\/withdraw"/);
 assert.match(admin,/transition\('withdraw'\)/);
 assert.match(admin,/confirm\('Να αποσυρθεί/);
 assert.match(grants,/GRANT EXECUTE ON FUNCTION hub\.withdraw_admin_player_quiz/);
 assert.match(boundary,/hub\.withdraw_admin_player_quiz\(text,text,uuid\)/);
 assert.match(schema,/WHERE status<>'withdrawn'/);
});
